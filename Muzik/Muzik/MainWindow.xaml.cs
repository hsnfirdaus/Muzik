// Copyright (c) Microsoft Corporation and Contributors.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT;
using WinRT.Interop;
using Windows.Storage.FileProperties;
using Windows.Media.Playback;
using Windows.Media.Core;
using Windows.UI.Core;
using static System.Collections.Specialized.BitVector32;
using System.Security.Cryptography.X509Certificates;
using Windows.Devices.Enumeration;
using System.Diagnostics;
using System.Reflection;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Muzik
{
	/// <summary>
	/// An empty window that can be used on its own or navigated to within a Frame.
	/// </summary>
	public sealed partial class MainWindow : Window
	{
		WindowsSystemDispatcherQueueHelper m_wsdqHelper;
		MicaController m_backdropController;
		SystemBackdropConfiguration m_configurationSource;
		Double seekerValue;
		MusicRepositories MusicRepositories { get; } = new MusicRepositories();
		public MainWindow()
		{
			this.InitializeComponent();
			Title = "Muzik";
			ExtendsContentIntoTitleBar = true;
			SetTitleBar(AppTitlebar);
			TrySetSystemBackdrop();

			MusicRepositories.mediaPlayer.PlaybackSession.PlaybackStateChanged += E_HandleStateChange;
			MusicRepositories.mediaPlayer.PlaybackSession.PositionChanged += E_HandlePositionChange;
			MusicRepositories.mediaPlayer.PlaybackSession.NaturalDurationChanged += E_HandleDurationChange;
			MusicRepositories.mediaPlaybackList.CurrentItemChanged += E_HandleItemChange;
			MusicRepositories.mediaPlaybackList.Items.VectorChanged += E_ReRenderPlaylist;
		}

		private void E_ReRenderPlaylist(IObservableVector<MediaPlaybackItem> sender, IVectorChangedEventArgs @event)
		{
			var items = sender.ToArray();
			List<MusicInfo> itemList = new();
			for (uint i = 0; i < items.Length; i++)
			{
				var data = items[i];
				MediaItemDisplayProperties displayProperties = data.GetDisplayProperties();
				MusicInfo append = new(
					i,
					displayProperties.MusicProperties.Title,
					displayProperties.MusicProperties.Artist,
					data.DurationLimit);
				itemList.Add(append);
			}
			_ = DispatcherQueue.TryEnqueue(() =>
			{
				PlaylistRepeater.ItemsSource = itemList;
			});
		}

		private void ChangePlaybackSymbol(Symbol symbol)
		{
			_ = DispatcherQueue.TryEnqueue(() =>
			{
				playButtonSymbol.Symbol = symbol;
			});
		}


		private void E_HandleStateChange(MediaPlaybackSession session, object obj)
		{
			if (session.PlaybackState == MediaPlaybackState.Playing)
			{
				ChangePlaybackSymbol(Symbol.Pause);
			}
			else
			{
				ChangePlaybackSymbol(Symbol.Play);
			}
		}

		private void E_HandlePositionChange(MediaPlaybackSession session, object obj)
		{
			_ = DispatcherQueue.TryEnqueue(() =>
			{
				seekerValue = session.Position.TotalSeconds;
				SliderDuration.Value = session.Position.TotalSeconds;
				CurrentDuration.Text = session.Position.ToString(@"mm\:ss");
			});
		}

		private void E_HandleDurationChange(MediaPlaybackSession session, object obj)
		{
			_ = DispatcherQueue.TryEnqueue(() =>
			{
				SliderDuration.Maximum = session.NaturalDuration.TotalSeconds;
				TotalDuration.Text = session.NaturalDuration.ToString(@"mm\:ss");
			});
		}

		private void E_HandleItemChange(MediaPlaybackList media, CurrentMediaPlaybackItemChangedEventArgs args)
		{
			var item = args.NewItem;
			if (item == null) return;

			var display = item.GetDisplayProperties();

			_ = DispatcherQueue.TryEnqueue(() =>
			{
				SongTitle.Text = display.MusicProperties.Title;
				SongArtist.Text = display.MusicProperties.Artist;
			});
		}

		private async void PlaylistItemClick(object sender, RoutedEventArgs e)
		{
			Button button = sender as Button;
			uint? index = button.Tag as uint?;
			if(index == null)
			{
				ContentDialog dialog = new()
				{
					Title = "Kesalahan!",
					Content = $"Index {index} tidak ada!.",
					CloseButtonText = "Ok"
				};
				dialog.XamlRoot = TopTitle.XamlRoot;
				ContentDialogResult _ = await dialog.ShowAsync();

				return;
			}

			MusicRepositories.mediaPlaybackList.MoveTo((uint)index);
		}

		private async void ShowAboutMe(object sender, RoutedEventArgs e)
		{
			ContentDialog dialog = new()
			{
				Title = "Informasi!",
				Content = $"Program Dibuat oleh Muhammad Hasan Firdaus.",
				CloseButtonText = "Ok"
			};
			dialog.XamlRoot = TopTitle.XamlRoot;
			ContentDialogResult _ = await dialog.ShowAsync();
		}

		private void SeekerChange(object sender, RangeBaseValueChangedEventArgs e)
		{
			if (e.NewValue != seekerValue)
			{
				TimeSpan to = TimeSpan.FromSeconds(e.NewValue);
				MusicRepositories.mediaPlayer.Position = to;
			}
		}

		private async void SelectFileDialog(object sender, RoutedEventArgs e)
		{
			FileOpenPicker openPicker = new();
			openPicker.SuggestedStartLocation = PickerLocationId.MusicLibrary;
			openPicker.FileTypeFilter.Add(".mp3");

			var hwnd = WindowNative.GetWindowHandle(this);
			InitializeWithWindow.Initialize(openPicker, hwnd);

			IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();

			foreach (var file in files)
			{
				if (file != null)
				{
					MusicRepositories.LoadFile(file);
				}
			}
		}

		private void PlayOrPause(object sender, RoutedEventArgs e)
		{
			MusicRepositories.Play();
		}

		private void PlayNextSong(object sender, RoutedEventArgs e)
		{
			MusicRepositories.Next();
		}

		private void PlayPrevSong(object sender, RoutedEventArgs e)
		{
			MusicRepositories.Prev();
		}

		bool TrySetSystemBackdrop()
		{
			if(Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported())
			{
				m_wsdqHelper = new WindowsSystemDispatcherQueueHelper();
				m_wsdqHelper.EnsureWindowsSystemDispatcherQueueController();

				m_configurationSource = new SystemBackdropConfiguration();
				this.Activated += Window_Activated;
				this.Closed += Window_Closed;
				((FrameworkElement)this.Content).ActualThemeChanged += Window_ThemeChanged;

				m_configurationSource.IsInputActive = true;
				SetConfigurationSourceTheme();

				m_backdropController = new Microsoft.UI.Composition.SystemBackdrops.MicaController();

				m_backdropController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
				m_backdropController.SetSystemBackdropConfiguration(m_configurationSource);
				return true;
			}

			return false;
		}

		private void Window_Activated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
		{
			m_configurationSource.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
		}

		private void Window_Closed(object sender, WindowEventArgs args)
		{
			if (m_backdropController != null)
			{
				m_backdropController.Dispose();
				m_backdropController = null;
			}
			this.Activated -= Window_Activated;
			m_configurationSource = null;
		}

		private void Window_ThemeChanged(FrameworkElement sender, object args)
		{
			if (m_configurationSource != null)
			{
				SetConfigurationSourceTheme();
			}
		}

		private void SetConfigurationSourceTheme()
		{
			switch (((FrameworkElement)this.Content).ActualTheme)
			{
				case ElementTheme.Dark: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Dark; break;
				case ElementTheme.Light: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Light; break;
				case ElementTheme.Default: m_configurationSource.Theme = Microsoft.UI.Composition.SystemBackdrops.SystemBackdropTheme.Default; break;
			}
		}
	}

	class WindowsSystemDispatcherQueueHelper
	{
		[StructLayout(LayoutKind.Sequential)]
		struct DispatcherQueueOptions
		{
			internal int dwSize;
			internal int threadType;
			internal int apartmentType;
		}

		[DllImport("CoreMessaging.dll")]
		private static extern int CreateDispatcherQueueController([In] DispatcherQueueOptions options, [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object dispatcherQueueController);

		object m_dispatcherQueueController = null;
		public void EnsureWindowsSystemDispatcherQueueController()
		{
			if (Windows.System.DispatcherQueue.GetForCurrentThread() != null)
			{
				// one already exists, so we'll just use it.
				return;
			}

			if (m_dispatcherQueueController == null)
			{
				DispatcherQueueOptions options;
				options.dwSize = Marshal.SizeOf(typeof(DispatcherQueueOptions));
				options.threadType = 2;    // DQTYPE_THREAD_CURRENT
				options.apartmentType = 2; // DQTAT_COM_STA

				CreateDispatcherQueueController(options, ref m_dispatcherQueueController);
			}
		}
	}

	public class MusicInfo
	{
		public MusicInfo(
			uint index,
			string title,
			string artist,
			TimeSpan? duration)
		{
			Index = index;
			Title = title;
			Artist = artist;
			Duration = duration;
		}
		public uint Index { get; set; }
		public string Title { get; set; }
		public string Artist { get; set; }
		public TimeSpan? Duration { get; set; }
	}

	public class MusicRepositories
	{
		public MediaPlayer mediaPlayer;
		public MediaPlaybackList mediaPlaybackList;
		public ObservableCollection<MusicInfo> Musics { get; } = new();

		public MusicRepositories()
		{
			mediaPlaybackList = new();
			mediaPlayer = new()
			{
				AutoPlay = true,
				Source = mediaPlaybackList
			};

		}

		public void Play()
		{
			if (mediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
			{
				mediaPlayer.Pause();
			}
			else
			{
				mediaPlayer.Play();
			}
		}
		public void Next()
		{
			mediaPlaybackList.MoveNext();
		}

		public void Prev()
		{
			mediaPlaybackList.MovePrevious();
		}

		public async void LoadFile(StorageFile file)
		{

			MusicProperties musicProperties = await file.Properties.GetMusicPropertiesAsync();

			if (musicProperties != null)
			{
				var item = new MediaPlaybackItem(MediaSource.CreateFromStorageFile(file));
				MediaItemDisplayProperties props = item.GetDisplayProperties();
				props.Type = Windows.Media.MediaPlaybackType.Music;

				if (musicProperties.Title == "" || musicProperties.Title == null)
				{
					ParsedMetadata parsed = ParseTitle(file.DisplayName);
					props.MusicProperties.Title = parsed.Title;
					props.MusicProperties.Artist = parsed.Artist;
				}
				else
				{
					props.MusicProperties.Title = musicProperties.Title;
					props.MusicProperties.Artist = musicProperties.Artist;
				}
				item.ApplyDisplayProperties(props);
				mediaPlaybackList.Items.Add(item);
			}
		}

		public class ParsedMetadata {
			public string Title { set; get; }
			public string Artist { set; get; }
		}

		private ParsedMetadata ParseTitle(string title)
		{
			string[] splitted = title.Split('-');

			var result = new ParsedMetadata { };
			if(splitted.Length > 1)
			{
				result.Title = splitted[1];
				result.Artist = splitted[0];
			}
			else
			{
				result.Title = title;
			}
			return result;
		}
	}
}
