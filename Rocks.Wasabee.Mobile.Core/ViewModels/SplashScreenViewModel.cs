﻿using Acr.UserDialogs;
using Microsoft.AppCenter.Analytics;
using MvvmCross;
using MvvmCross.Commands;
using MvvmCross.Navigation;
using MvvmCross.Plugin.Messenger;
using MvvmCross.ViewModels;
using Newtonsoft.Json;
using Rocks.Wasabee.Mobile.Core.Infra.Constants;
using Rocks.Wasabee.Mobile.Core.Infra.Databases;
using Rocks.Wasabee.Mobile.Core.Infra.Security;
using Rocks.Wasabee.Mobile.Core.Messages;
using Rocks.Wasabee.Mobile.Core.Models;
using Rocks.Wasabee.Mobile.Core.Models.AuthTokens.Google;
using Rocks.Wasabee.Mobile.Core.Models.Users;
using Rocks.Wasabee.Mobile.Core.Services;
using Rocks.Wasabee.Mobile.Core.Settings.Application;
using Rocks.Wasabee.Mobile.Core.Settings.User;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xamarin.Essentials;
using Xamarin.Essentials.Interfaces;

namespace Rocks.Wasabee.Mobile.Core.ViewModels
{
    public class SplashScreenNavigationParameter
    {
        public bool DoDataRefreshOnly { get; }

        public SplashScreenNavigationParameter(bool doDataRefreshOnly)
        {
            DoDataRefreshOnly = doDataRefreshOnly;
        }
    }

    public class SplashScreenViewModel : BaseViewModel, IMvxViewModel<SplashScreenNavigationParameter>
    {
        private readonly IConnectivity _connectivity;
        private readonly IPreferences _preferences;
        private readonly IVersionTracking _versionTracking;
        private readonly IAuthentificationService _authentificationService;
        private readonly IMvxNavigationService _navigationService;
        private readonly IMvxMessenger _messenger;
        private readonly ISecureStorage _secureStorage;
        private readonly IAppSettings _appSettings;
        private readonly IUserSettingsService _userSettingsService;
        private readonly IUserDialogs _userDialogs;
        private readonly WasabeeApiV1Service _wasabeeApiV1Service;
        private readonly UsersDatabase _usersDatabase;
        private readonly OperationsDatabase _operationsDatabase;
        private readonly TeamsDatabase _teamsDatabase;

        private bool _working;
        private GoogleToken _googleToken;
        private bool _isBypassingGoogleAndWasabeeLogin = false;

        private SplashScreenNavigationParameter _parameter;

        public SplashScreenViewModel(IConnectivity connectivity, IPreferences preferences, IVersionTracking versionTracking,
            IAuthentificationService authentificationService, IMvxNavigationService navigationService, IMvxMessenger messenger,
            ISecureStorage secureStorage, IAppSettings appSettings, IUserSettingsService userSettingsService, IUserDialogs userDialogs,
            WasabeeApiV1Service wasabeeApiV1Service, UsersDatabase usersDatabase, OperationsDatabase operationsDatabase, TeamsDatabase teamsDatabase)
        {
            _connectivity = connectivity;
            _preferences = preferences;
            _versionTracking = versionTracking;
            _authentificationService = authentificationService;
            _navigationService = navigationService;
            _messenger = messenger;
            _secureStorage = secureStorage;
            _appSettings = appSettings;
            _userSettingsService = userSettingsService;
            _userDialogs = userDialogs;
            _wasabeeApiV1Service = wasabeeApiV1Service;
            _usersDatabase = usersDatabase;
            _operationsDatabase = operationsDatabase;
            _teamsDatabase = teamsDatabase;
        }

        public void Prepare(SplashScreenNavigationParameter parameter)
        {
            _parameter = parameter;
        }

        public override void Start()
        {
            base.Start();

            LoggingService.Trace("Starting SplashScreenViewModel");
            Analytics.TrackEvent(GetType().Name);

            AppEnvironnement = _preferences.Get(ApplicationSettingsConstants.AppEnvironnement, "unknown_env");
            var appVersion = _versionTracking.CurrentVersion;
            DisplayVersion = AppEnvironnement != "release" ? $"{AppEnvironnement} - v{appVersion}" : $"v{appVersion}";
        }

        public override Task Initialize()
        {
            LoggingService.Trace("Initializing SplashScreenViewModel");

            // TODO Handle app opening from notification


            LoadingStepLabel = "Application loading...";
            _connectivity.ConnectivityChanged += ConnectivityOnConnectivityChanged;

            RememberServerChoice = _preferences.Get(UserSettingsKeys.RememberServerChoice, false);

            return Task.CompletedTask;
        }

        public override async void ViewAppeared()
        {
            base.ViewAppeared();

            if (!_working)
            {
                _working = true;
                await StartApplication();
            }
        }

        #region Properties

        public bool IsLoading { get; set; }
        public bool IsConnected { get; set; }
        public bool IsLoginVisible { get; set; }
        public bool IsAuthInError { get; set; }
        public bool IsSelectingServer { get; set; }
        public bool RememberServerChoice { get; set; }
        public bool HasNoTeamOrOpsAssigned { get; set; }
        public string LoadingStepLabel { get; set; }
        public string AppEnvironnement { get; set; }
        public string DisplayVersion { get; set; }
        public string ErrorMessage { get; set; }

        private ServerItem _selectedServerItem = ServerItem.Undefined;
        public ServerItem SelectedServerItem
        {
            get => _selectedServerItem;
            set
            {
                if (SetProperty(ref _selectedServerItem, value))
                {
                    if (SelectedServerItem.Server != WasabeeServer.Undefined)
                        _appSettings.Server = SelectedServerItem.Server;
                }
            }
        }

        public MvxObservableCollection<ServerItem> ServersCollection => new MvxObservableCollection<ServerItem>()
        {
            new ServerItem("America", WasabeeServer.US, "US.png"),
            new ServerItem("Europe", WasabeeServer.EU, "EU.png"),
            new ServerItem("Asia/Pacific", WasabeeServer.APAC, "APAC.png")
        };

        #endregion

        #region Commands

        public IMvxAsyncCommand ConnectUserCommand => new MvxAsyncCommand(ConnectUser);
        private async Task ConnectUser()
        {
            LoggingService.Trace("Executing SplashScreenViewModel.ConnectUserCommand");

            if (!IsConnected)
            {
                IsLoading = false;
                IsLoginVisible = true;
                IsSelectingServer = false;

                return;
            }

            if (IsLoading) return;

            IsLoginVisible = false;
            IsLoading = true;
            LoadingStepLabel = "Logging in...";

            await Task.Delay(TimeSpan.FromMilliseconds(300));

            await _usersDatabase.DeleteAllData();

            var wasabeeCookie = await _secureStorage.GetAsync(SecureStorageConstants.WasabeeCookie);
            if (!string.IsNullOrWhiteSpace(wasabeeCookie) && RememberServerChoice)
            {
                await BypassGoogleAndWasabeeLogin();
                return;
            }

            _googleToken = await _authentificationService.GoogleLoginAsync();
            if (_googleToken != null)
            {
                LoadingStepLabel = "Google login success...";
                await Task.Delay(TimeSpan.FromMilliseconds(300));

                var savedServerChoice = _preferences.Get(UserSettingsKeys.SavedServerChoice, string.Empty);
                if (ServersCollection.Any(x => x.Server.ToString().Equals(savedServerChoice)))
                    SelectedServerItem = ServersCollection.First(x => x.Server.ToString().Equals(savedServerChoice));

                if (SelectedServerItem.Server == WasabeeServer.Undefined)
                    ChangeServerCommand.Execute();
                else
                    await ConnectWasabee();
            }
            else
            {
                ErrorMessage = "Google login failed !";
                IsAuthInError = true;
                IsLoginVisible = true;
            }

            IsLoading = false;
        }

        public IMvxAsyncCommand<ServerItem> ChooseServerCommand => new MvxAsyncCommand<ServerItem>(ChooseServer);
        private async Task ChooseServer(ServerItem serverItem)
        {
            LoggingService.Trace($"Executing SplashScreenViewModel.ChooseServerCommand({serverItem})");

            IsSelectingServer = false;
            SelectedServerItem = serverItem;

            _preferences.Set(UserSettingsKeys.CurrentServer, SelectedServerItem.Server.ToString());

            if (!_isBypassingGoogleAndWasabeeLogin)
                await ConnectWasabee();
            else
                await BypassGoogleAndWasabeeLogin();
        }

        public IMvxCommand ChangeServerCommand => new MvxCommand(ChangeServer);
        private void ChangeServer()
        {
            LoggingService.Trace("Executing SplashScreenViewModel.ChangeServerCommand");

            HasNoTeamOrOpsAssigned = false;

            LoadingStepLabel = "Choose your server :";
            IsLoading = false;
            IsLoginVisible = false;
            IsSelectingServer = true;
        }

        public IMvxAsyncCommand RetryTeamLoadingCommand => new MvxAsyncCommand(RetryTeamLoading);
        private async Task RetryTeamLoading()
        {
            LoggingService.Trace("Executing SplashScreenViewModel.RetryTeamLoadingCommand");

            if (IsLoading) return;

            HasNoTeamOrOpsAssigned = false;
            await ConnectWasabee();
        }

        public IMvxAsyncCommand ChangeAccountCommand => new MvxAsyncCommand(ChangeAccount);
        private async Task ChangeAccount()
        {
            LoggingService.Trace("Executing SplashScreenViewModel.ChangeAccountCommand");

            IsLoading = false;
            HasNoTeamOrOpsAssigned = false;
            IsLoginVisible = true;
            SelectedServerItem = ServerItem.Undefined;

            _preferences.Remove(UserSettingsKeys.RememberServerChoice);
            _preferences.Remove(UserSettingsKeys.SavedServerChoice);

            await ConnectUserCommand.ExecuteAsync();
        }

        public IMvxCommand RememberChoiceCommand => new MvxCommand(() =>
        {
            LoggingService.Trace("Executing SplashScreenViewModel.RememberChoiceCommand");

            RememberServerChoice = !RememberServerChoice;
        });

        #endregion

        #region Private methods

        private async void ConnectivityOnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
        {
            await StartApplication();
        }

        private async Task StartApplication()
        {
            IsConnected = _connectivity.ConnectionProfiles.Any() && _connectivity.NetworkAccess == NetworkAccess.Internet;

            if (_parameter != null && _parameter.DoDataRefreshOnly)
            {
                await BypassGoogleAndWasabeeLogin();

                return;
            }

            await ConnectUserCommand.ExecuteAsync();
        }

        private async Task ConnectWasabee()
        {
            IsLoading = true;
            LoadingStepLabel = $"Contacting '{SelectedServerItem.Name}' Wasabee server...";
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            var wasabeeUserModel = await _authentificationService.WasabeeLoginAsync(_googleToken);
            if (wasabeeUserModel != null)
            {
                Mvx.IoCProvider.Resolve<IMvxMessenger>().Publish(new UserLoggedInMessage(this));

                if (RememberServerChoice)
                {
                    _preferences.Set(UserSettingsKeys.RememberServerChoice, RememberServerChoice);
                    _preferences.Set(UserSettingsKeys.SavedServerChoice, SelectedServerItem.Server.ToString());
                }

                await _usersDatabase.SaveUserModel(wasabeeUserModel);

                _userSettingsService.SaveLoggedUserGoogleId(wasabeeUserModel.GoogleId);
                _userSettingsService.SaveIngressName(wasabeeUserModel.IngressName);

                await FinishLogin(wasabeeUserModel);
            }
            else
            {
                ErrorMessage = "Wasabee login failed !";
                IsAuthInError = true;
                IsLoading = false;
                IsLoginVisible = true;
            }
        }

        private async Task BypassGoogleAndWasabeeLogin()
        {
            _isBypassingGoogleAndWasabeeLogin = true;

            var savedServerChoice = _preferences.Get(UserSettingsKeys.SavedServerChoice, string.Empty);
            var currentServer = _preferences.Get(UserSettingsKeys.CurrentServer, string.Empty);

            if (ServersCollection.Any(x => x.Server.ToString().Equals(savedServerChoice)))
                SelectedServerItem = ServersCollection.First(x => x.Server.ToString().Equals(savedServerChoice));
            else if (ServersCollection.Any(x => x.Server.ToString().Equals(currentServer)))
                SelectedServerItem = ServersCollection.First(x => x.Server.ToString().Equals(currentServer));

            if (SelectedServerItem.Server == WasabeeServer.Undefined)
                ChangeServerCommand.Execute();
            else
            {
                IsLoading = true;
                LoadingStepLabel = $"Contacting '{SelectedServerItem.Name}' Wasabee server...";
                await Task.Delay(TimeSpan.FromMilliseconds(300));

                if (RememberServerChoice)
                {
                    _preferences.Set(UserSettingsKeys.RememberServerChoice, RememberServerChoice);
                    _preferences.Set(UserSettingsKeys.SavedServerChoice, SelectedServerItem.Server.ToString());
                }


                var result = await _wasabeeApiV1Service.User_GetUserInformations();
                if (!string.IsNullOrWhiteSpace(result))
                {
                    var wasabeeUserModel = JsonConvert.DeserializeObject<UserModel>(result);
                    if (wasabeeUserModel == null)
                    {
                        ErrorMessage = "Wasabee login failed !";
                        IsAuthInError = true;
                        IsLoginVisible = true;
                        IsLoading = false;

                        return;
                    }

                    await _usersDatabase.SaveUserModel(wasabeeUserModel);
                    await FinishLogin(wasabeeUserModel);
                }
            }
        }

        private async Task FinishLogin(UserModel userModel)
        {
            LoadingStepLabel = $"Welcome {userModel.IngressName}";
            await Task.Delay(TimeSpan.FromSeconds(1));

            try
            {
                if ((userModel.Teams == null || !userModel.Teams.Any()) &&
                    (userModel.Ops == null || !userModel.Ops.Any()))
                {
                    IsLoading = false;
                    HasNoTeamOrOpsAssigned = true;
                }
                else
                {
                    await PullDataFromServer(userModel);

                    await _navigationService.Navigate<RootViewModel>();
                    await _navigationService.Close(this);
                }
            }
            catch (Exception e)
            {
                LoggingService.Error(e, "Error Executing SplashScreenViewModel.FinishLogin");

                IsAuthInError = true;
                IsLoginVisible = true;
                IsLoading = false;
                ErrorMessage = "Error loading Wasabee OPs data";
            }
        }

        private async Task PullDataFromServer(UserModel userModel)
        {
            LoadingStepLabel = "Harvesting beehive,\r\n" +
                               "Please wait...";
            await Task.Delay(TimeSpan.FromMilliseconds(300));

            await _teamsDatabase.DeleteAllData();
            await _operationsDatabase.DeleteAllData();

            if (userModel.Teams != null && userModel.Teams.Any())
            {
                var teamIds = userModel.Teams
                    .Where(t => t.State == "On")
                    .Select(t => t.Id)
                    .ToList();

                foreach (var id in teamIds)
                {
                    var team = await _wasabeeApiV1Service.GetTeam(id);
                    if (team != null)
                        await _teamsDatabase.SaveTeamModel(team);
                }
            }

            if (userModel.Ops != null && userModel.Ops.Any())
            {
                var opsIds = userModel.Ops
                    .Select(x => x.Id)
                    .ToList();

                var selectedOp = _preferences.Get(UserSettingsKeys.SelectedOp, string.Empty);
                if (selectedOp == string.Empty || opsIds.All(id => !id.Equals(selectedOp)))
                {
                    var id = opsIds.First();
                    _preferences.Set(UserSettingsKeys.SelectedOp, id);
                    selectedOp = id;
                }

                var op = await _wasabeeApiV1Service.Operations_GetOperation(selectedOp);
                if (op != null)
                    await _operationsDatabase.SaveOperationModel(op);

                _ = Task.Factory.StartNew(async () =>
                {
                    _userDialogs.Toast("Your OPs are loading in background");

                    foreach (var id in opsIds.Except(new[] { selectedOp }))
                    {
                        op = await _wasabeeApiV1Service.Operations_GetOperation(id);
                        if (op != null)
                        {
                            await _operationsDatabase.SaveOperationModel(op);
                            _messenger.Publish(new NewOpAvailableMessage(this));
                        }
                    }

                    _userDialogs.Toast("OPs loaded succesfully");
                }).ConfigureAwait(false);
            }
            else
            {
                _preferences.Set(UserSettingsKeys.SelectedOp, string.Empty);
            }
        }

        #endregion
    }
}