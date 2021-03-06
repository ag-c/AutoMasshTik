﻿using AutoMasshTik.Engine;
using AutoMasshTik.Engine.Actions;
using AutoMasshTik.Engine.Core;
using AutoMasshTik.Engine.Models;
using AutoMasshTik.Engine.Services.Abstract;
using AutoMasshTik.Engine.States;
using NLog;
using Sharp.Redux;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AutoMasshTik.UI.ViewModels
{
    public class MainViewModel: NotifiableObject
    {
        static readonly ILogger logger = LogManager.GetCurrentClassLogger();

        readonly IAppReduxDispatcher appReduxDispatcher;
        readonly IUpdater updater;
        readonly IAppUpdater appUpdater;
        readonly Func<Server, ServerViewModel> serverViewModelFactory;
        public string ServersText { get; private set; }
        public Server[] ServerModels { get; private set; }
        public string Username { get; private set; }
        public string Password { get; private set; }
        public int Port { get; private set; }
        public  bool IsUpdating { get; private set; }
        public string OperationInProgress { get; private set; }
        public bool ShowPassword { get; private set; }
        public ObservableCollection<ServerViewModel> Servers { get; }
        public RelayCommand<UpdateMode> StartUpdateCommand { get; }
        public RelayCommand StopUpdateCommand { get; }
        public RelayCommand ToggleShowPasswordCommand { get;  }
        public  bool NewVersionAvailable { get; private set; }
        bool isUpdatingState;
        CancellationTokenSource cts;
        RootState state;
        public MainViewModel(IAppReduxDispatcher appReduxDispatcher, IUpdater updater, IAppUpdater appUpdater, Func<Server, ServerViewModel> serverViewModelFactory)
        {
            logger.Info($"Welcome to AutoMasshTik v{typeof(MainViewModel).Assembly.GetName().Version}");
            this.appReduxDispatcher = appReduxDispatcher;
            this.updater = updater;
            this.appUpdater = appUpdater;
            this.serverViewModelFactory = serverViewModelFactory;
            state = appReduxDispatcher.InitialState;
            ServerModels = state.Servers;
            Servers = new ObservableCollection<ServerViewModel>();
            StartUpdateCommand = new RelayCommand<UpdateMode>(StartUpdate, m => !IsUpdating);
            StopUpdateCommand = new RelayCommand(StopUpdate, () => IsUpdating);
            ToggleShowPasswordCommand = new RelayCommand(ToggleShowPassword);
            this.appReduxDispatcher.StateChanged += AppReduxDispatcher_StateChanged;
        }
        void ToggleShowPassword()
        {
            appReduxDispatcher.Dispatch(new ToggleShowPasswordAction());
        }
        async void StartUpdate(UpdateMode mode)
        {
            appReduxDispatcher.Dispatch(new StartUpdateAction(mode));
            cts?.Cancel();
            cts = new CancellationTokenSource();
            try
            {
                await updater.UpdateAsync(mode, state.Servers, Username, Password, Port, useCredentials: true, cts.Token);
            }
            finally
            {
                appReduxDispatcher.Dispatch(new StopUpdateAction(false));
            }
        }
        void StopUpdate()
        {
            cts?.Cancel();
        }
        public void Start()
        {
            appReduxDispatcher.Start();
            var ignore = CheckForUpdatesAsync();
        }

        internal async Task CheckForUpdatesAsync()
        {
            try
            {
                var result = await appUpdater.GetLatestVersionAsync(CancellationToken.None);
                if (result.HasValue && result.Value.CurrentVersion != result.Value.FutureVersion)
                {
                    logger.Info($"Updater will fetch version {result.Value.FutureVersion}");
                    await appUpdater.UpdateAsync(CancellationToken.None);
                    NewVersionAvailable = true;
                    logger.Info("Update finished, restart required");
                }
                else
                {
                    logger.Info("Got no response from updater or version is current");
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Failed checking for updates");
            }
        }
        private void AppReduxDispatcher_StateChanged(object sender, StateChangedEventArgs<RootState> e)
        {
            isUpdatingState = true;
            try
            {
                state = e.State;
                ServerModels = state.Servers;
                ServersText = string.Join(Environment.NewLine, e.State.Servers.Select(s => s.Url));
                ReduxMerger.MergeList<int, Server, ServerViewModel>(state.Servers, Servers, serverViewModelFactory);
                Username = e.State.Username;
                Password = e.State.Password;
                IsUpdating = e.State.IsUpdating;
                Port = e.State.Port;
                OperationInProgress = e.State.OperationInProgress;
                ShowPassword = state.ShowPassword;
            }
            finally
            {
                isUpdatingState = false;
            }
        }
        protected override void OnPropertyChanged(string name)
        {
            if (!isUpdatingState)
            {
                switch (name)
                {
                    case nameof(ServersText):
                        appReduxDispatcher.Dispatch(new ServersChangedAction(ServersText));
                        break;
                    case nameof(Username):
                        appReduxDispatcher.Dispatch(new UsernameChangedAction(Username));
                        break;
                    case nameof(Password):
                        appReduxDispatcher.Dispatch(new PasswordChangedAction(Password));
                        break;
                    case nameof(Port):
                        appReduxDispatcher.Dispatch(new PortChangedAction(Port));
                        break;
                }
            }
            switch (name)
            {
                case nameof(IsUpdating):
                    StartUpdateCommand.RaiseCanExecuteChanged();
                    StopUpdateCommand.RaiseCanExecuteChanged();
                    break;
            }
            base.OnPropertyChanged(name);
        }
    }
}
