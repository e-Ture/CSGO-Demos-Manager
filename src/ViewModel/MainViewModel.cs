using System;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using CSGO_Demos_Manager.Internals;
using CSGO_Demos_Manager.Views;
using GalaSoft.MvvmLight.Messaging;
using WpfPageTransitions;
using CSGO_Demos_Manager.Messages;
using CSGO_Demos_Manager.Services;
using MahApps.Metro.Controls.Dialogs;

namespace CSGO_Demos_Manager.ViewModel
{
	public class MainViewModel : ViewModelBase
	{

		# region Properties

		private PageTransition _currentPage;

		private bool _isSettingsOpen;

		public RelayCommand ToggleSettingsFlyOutCommand { get; set; }

		public RelayCommand SettingsFlyoutClosedCommand { get; set; }

		ObservableCollection<string> _folders;

		private string _selectedFolder;

		private RelayCommand _addFolderCommand;

		private RelayCommand<string> _removeFolderCommand;

		public string CreditsText => AppSettings.APP_NAME + " " + AppSettings.APP_VERSION + " by " + AppSettings.AUTHOR;

		private readonly DialogService _dialogService;

		private RelayCommand _windowLoadedCommand;

		private RelayCommand _windowClosedCommand;

		private readonly ICacheService _cacheService;

		#endregion

		#region Accessors

		public PageTransition CurrentPage
		{
			get { return _currentPage; }
			set { Set(() => CurrentPage, ref _currentPage, value); }
		}

		public bool IsSettingsOpen
		{
			get { return _isSettingsOpen; }
			set { Set(() => IsSettingsOpen, ref _isSettingsOpen, value); }
		}

		public string SelectedFolder
		{
			get { return _selectedFolder; }
			set { Set(() => SelectedFolder, ref _selectedFolder, value); }
		}

		public ObservableCollection<string> Folders
		{
			get { return _folders; }
			set { Set(() => Folders, ref _folders, value); }
		}

		#endregion

		#region Commands

		/// <summary>
		/// Command fired when MainWindow is loaded
		/// </summary>
		public RelayCommand WindowLoaded
		{
			get
			{
				return _windowLoadedCommand
					?? (_windowLoadedCommand = new RelayCommand(
					async () =>
					{
						if (Folders.Count == 0)
						{
							await _dialogService.ShowMessageAsync("It seems that CSGO is not installed on your main hard drive. The defaults \"csgo\" and \"replays\" can not be found. Please add folders from the settings.", MessageDialogStyle.Affirmative);
						}

						// Check for 1st launch or upgrade that required cache clear
						if (_cacheService.ContainsDemos())
						{
							if ((string.IsNullOrEmpty(Properties.Settings.Default.ApplicationVersion))
							|| (!string.IsNullOrEmpty(Properties.Settings.Default.ApplicationVersion) && new Version(Properties.Settings.Default.ApplicationVersion).CompareTo(AppSettings.APP_VERSION) < 0 && AppSettings.REQUIRE_CLEAR_CACHE))
							{
								var saveCustomData = await _dialogService.ShowMessageAsync("This update required to clear custom data from cache (your suspects list will not be removed). Do you want to save your custom data? ", MessageDialogStyle.AffirmativeAndNegative);
								if (saveCustomData == MessageDialogResult.Affirmative)
								{
									SaveFileDialog saveCustomDataDialog = new SaveFileDialog
									{
										FileName = "backup.json",
										Filter = "JSON file (*.json)|*.json"
									};

									if (saveCustomDataDialog.ShowDialog() == DialogResult.OK)
									{
										try
										{
											await _cacheService.CreateBackupCustomDataFile(saveCustomDataDialog.FileName);
											await _dialogService.ShowMessageAsync("The backup file has been created, you have to re-import your custom data from settings.", MessageDialogStyle.Affirmative);
										}
										catch (Exception e)
										{
											Logger.Instance.Log(e);
											await _dialogService.ShowErrorAsync("An error occured while exporting custom data.", MessageDialogStyle.Affirmative);
										}
									}
								}
								// Clear cache even if user didn't want to backup his custom data
								await _cacheService.ClearDemosFile();
							}
						}

						// Update the user version
						Properties.Settings.Default.ApplicationVersion = AppSettings.APP_VERSION.ToString();
						Properties.Settings.Default.Save();

						// Check for update
						if (Properties.Settings.Default.EnableCheckUpdate)
						{
							bool isUpdateAvailable = await CheckUpdate();
							if (isUpdateAvailable)
							{
								var download = await _dialogService.ShowMessageAsync("A new version is available. Do you want to download it?", MessageDialogStyle.AffirmativeAndNegative);
								if (download == MessageDialogResult.Affirmative)
								{
									System.Diagnostics.Process.Start(AppSettings.APP_WEBSITE);
								}
							}
						}

						// Notify the HomeViewModel that it can now load demos data
						MainWindowLoadedMessage msg = new MainWindowLoadedMessage();
						Messenger.Default.Send(msg);
					}));
			}
		}

		/// <summary>
		/// Command fired when Main Window is closed
		/// </summary>
		public RelayCommand WindowClosed
		{
			get
			{
				return _windowClosedCommand
					?? (_windowClosedCommand = new RelayCommand(
					() =>
					{
						if (Folders.Count == 0)
						{
							Properties.Settings.Default.LastFolder = string.Empty;
							Properties.Settings.Default.Save();
						}
					}));
			}
		}

		/// <summary>
		/// Command to add a new folder
		/// </summary>
		public RelayCommand AddFolderCommand
		{
			get
			{
				return _addFolderCommand
					?? (_addFolderCommand = new RelayCommand(
					() =>
					{
						FolderBrowserDialog folderDialog = new FolderBrowserDialog
						{
							SelectedPath = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))
					};

						DialogResult result = folderDialog.ShowDialog();
						if (result != DialogResult.OK) return;
						string path = Path.GetFullPath(folderDialog.SelectedPath).ToLower();
						if (Folders.Contains(path)) return;
						if (AppSettings.AddFolder(path))
						{
							Folders.Add(path);
						}
					}));
			}
		}

		/// <summary>
		/// Command to remove a specific folder
		/// </summary>
		public RelayCommand<string> RemoveFolderCommand
		{
			get
			{
				return _removeFolderCommand
					?? (_removeFolderCommand = new RelayCommand<string>(
					f =>
					{
						if (!RemoveFolderCommand.CanExecute(null))
						{
							return;
						}
						Folders.Remove(f);
						AppSettings.RemoveFolder(f);
					},
					f => SelectedFolder != null));
			}
		}

		#endregion

		public MainViewModel(DialogService dialogService, ICacheService cacheService)
		{
			_dialogService = dialogService;
			_cacheService = cacheService;

			CurrentPage = new PageTransition();
			HomeView homeView = new HomeView();
			CurrentPage.ShowPage(homeView);

			Folders = AppSettings.GetFolders();

			ToggleSettingsFlyOutCommand = new RelayCommand(() =>
			{
				IsSettingsOpen = true;
			}, () => IsSettingsOpen == false);

			SettingsFlyoutClosedCommand = new RelayCommand(() =>
			{
				IsSettingsOpen = false;

				Folders.Clear();
				Folders = AppSettings.GetFolders();

				RefreshDemosMessage msg = new RefreshDemosMessage();
				Messenger.Default.Send(msg);
			});
		}

		private static async Task<bool> CheckUpdate()
		{
			using (var httpClient = new HttpClient())
			{
				string url = AppSettings.APP_WEBSITE + "/update";
				HttpResponseMessage result = await httpClient.GetAsync(url);
				string version = await result.Content.ReadAsStringAsync();
				Version lastVersion = new Version(version);

				var resultCompare = AppSettings.APP_VERSION.CompareTo(lastVersion);
				if (resultCompare < 0)
				{
					return true;
				}
				return false;
			}
		}
	}
}