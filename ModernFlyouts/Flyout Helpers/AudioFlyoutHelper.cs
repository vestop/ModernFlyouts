﻿using ModernFlyouts.Utilities;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace ModernFlyouts
{
    public class AudioFlyoutHelper : FlyoutHelperBase
    {
        private AudioDeviceNotificationClient client;
        private MMDeviceEnumerator enumerator;
        private MMDevice device;
        private VolumeControl volumeControl;
        private SessionsPanel sessionsPanel;
        private TextBlock noDeviceMessageBlock;
        private bool _isinit = false;
        private bool _SMTCAvail = false;
        private bool isVolumeFlyout = true;

        public override event ShowFlyoutEventHandler ShowFlyoutRequested;

        #region Properties

        public static readonly DependencyProperty ShowGSMTCInVolumeFlyoutProperty =
            DependencyProperty.Register(
                nameof(ShowGSMTCInVolumeFlyout),
                typeof(bool),
                typeof(AudioFlyoutHelper),
                new PropertyMetadata(true, OnShowGSMTCInVolumeFlyoutChanged));

        public bool ShowGSMTCInVolumeFlyout
        {
            get => (bool)GetValue(ShowGSMTCInVolumeFlyoutProperty);
            set => SetValue(ShowGSMTCInVolumeFlyoutProperty, value);
        }

        private static void OnShowGSMTCInVolumeFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var helper = d as AudioFlyoutHelper;
            bool value = (bool)e.NewValue;
            if (helper.isVolumeFlyout)
            {
                helper.SecondaryContentVisible = helper._SMTCAvail ? value : false;
            }
            else
            {
                helper.SecondaryContentVisible = helper._SMTCAvail;
            }

            AppDataHelper.ShowGSMTCInVolumeFlyout = value;
        }

        public static readonly DependencyProperty ShowVolumeControlInGSMTCFlyoutProperty =
            DependencyProperty.Register(
                nameof(ShowVolumeControlInGSMTCFlyout),
                typeof(bool),
                typeof(AudioFlyoutHelper),
                new PropertyMetadata(true, OnShowVolumeControlInGSMTCFlyoutChanged));

        public bool ShowVolumeControlInGSMTCFlyout
        {
            get => (bool)GetValue(ShowVolumeControlInGSMTCFlyoutProperty);
            set => SetValue(ShowVolumeControlInGSMTCFlyoutProperty, value);
        }

        private static void OnShowVolumeControlInGSMTCFlyoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var helper = d as AudioFlyoutHelper;
            bool value = (bool)e.NewValue;
            if (helper.isVolumeFlyout)
            {
                helper.PrimaryContentVisible = true;
            }
            else
            {
                helper.PrimaryContentVisible = value;
            }

            AppDataHelper.ShowVolumeControlInGSMTCFlyout = value;
        }

        #endregion

        public AudioFlyoutHelper()
        {
           Initialize();
        }

        public void Initialize()
        {
            AlwaysHandleDefaultFlyout = true;

            ShowGSMTCInVolumeFlyout = AppDataHelper.ShowGSMTCInVolumeFlyout;
            ShowVolumeControlInGSMTCFlyout = AppDataHelper.ShowVolumeControlInGSMTCFlyout;

            #region Creating Volume Control

            volumeControl = new VolumeControl();
            volumeControl.VolumeButton.Click += VolumeButton_Click;
            volumeControl.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
            volumeControl.VolumeSlider.PreviewMouseWheel += VolumeSlider_PreviewMouseWheel;

            noDeviceMessageBlock = new TextBlock()
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 18.0,
                Margin = new Thickness(20),
                Text = "No Connected Audio Devices"
            };
            noDeviceMessageBlock.SetResourceReference(TextBlock.StyleProperty, "BaseTextBlockStyle");

            #endregion

            #region Creating Session Controls

            sessionsPanel = new SessionsPanel();
            SecondaryContent = sessionsPanel;

            try { SetupSMTCAsync(); } catch { }

            #endregion
            
            PrimaryContent = volumeControl;
            client = new AudioDeviceNotificationClient();

            enumerator = new MMDeviceEnumerator();
            enumerator.RegisterEndpointNotificationCallback(client);

            if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                UpdateDevice(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia));
            }

            OnEnabled();
            _isinit = true;
        }

        public void OnExternalUpdated(bool isMediaKey)
        {
            if ((!isMediaKey && device != null) || (isMediaKey && _SMTCAvail))
            {
                isVolumeFlyout = !isMediaKey;

                if (isVolumeFlyout)
                {
                    PrimaryContentVisible = true;
                    SecondaryContentVisible = _SMTCAvail ? ShowGSMTCInVolumeFlyout : false;
                }
                else
                {
                    PrimaryContentVisible = ShowVolumeControlInGSMTCFlyout;
                    SecondaryContentVisible = _SMTCAvail;
                }

                ShowFlyoutRequested?.Invoke(this);
            }
        }

        #region Volume

        private void Client_DefaultDeviceChanged(object sender, string e)
        {
            MMDevice mmdevice = string.IsNullOrEmpty(e) ? null : enumerator.GetDevice(e);
            UpdateDevice(mmdevice);
        }

        private void UpdateDevice(MMDevice mmdevice)
        {
            if (device != null)
            {
                device.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
            }

            device = mmdevice;
            if (device != null)
            {
                UpdateVolume(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100);
                device.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
                Dispatcher.Invoke(() => PrimaryContent = volumeControl);
            }
            else { Dispatcher.Invoke(() => PrimaryContent = noDeviceMessageBlock); }
        }

        private void AudioEndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data)
        {
            UpdateVolume(data.MasterVolume * 100);
        }

        private bool _isInCodeValueChange = false; //Prevents a LOOP between changing volume.

        private void UpdateVolume(double volume)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateVolumeGlyph(volume);
                volumeControl.textVal.Text = Math.Round(volume).ToString("00");
                _isInCodeValueChange = true;
                volumeControl.VolumeSlider.Value = volume;
                _isInCodeValueChange = false;
            });
        }

        private void UpdateVolumeGlyph(double volume)
        {
            if (device != null && !device.AudioEndpointVolume.Mute)
            {
                volumeControl.VolumeShadowGlyph.Visibility = Visibility.Visible;
                if (volume >= 66)
                    volumeControl.VolumeGlyph.Glyph = CommonGlyphs.Volume3;
                else if (volume < 1)
                    volumeControl.VolumeGlyph.Glyph = CommonGlyphs.Volume0;
                else if (volume < 33)
                    volumeControl.VolumeGlyph.Glyph = CommonGlyphs.Volume1;
                else if (volume < 66)
                    volumeControl.VolumeGlyph.Glyph = CommonGlyphs.Volume2;

                volumeControl.textVal.ClearValue(TextBlock.ForegroundProperty);
                volumeControl.VolumeSlider.IsEnabled = true;

            }
            else
            {
                volumeControl.textVal.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseMediumBrush");
                volumeControl.VolumeSlider.IsEnabled = false;
                volumeControl.VolumeShadowGlyph.Visibility = Visibility.Collapsed;
                volumeControl.VolumeGlyph.Glyph = CommonGlyphs.Mute;
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isInCodeValueChange)
            {
                var value = Math.Truncate(e.NewValue);
                var oldValue = Math.Truncate(e.OldValue);

                if (value == oldValue)
                {
                    return;
                }

                if (device != null)
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(value / 100);
                    device.AudioEndpointVolume.Mute = false;
                }

                e.Handled = true;
            }
        }

        private void VolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (device != null)
            {
                device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
            }
        }

        private void VolumeSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var slider = sender as Slider;
            var value = Math.Truncate(slider.Value);
            var change = e.Delta / 120;

            var volume = value + change;

            if (volume > 100 || volume < 0)
            {
                return;
            }


            if (device != null)
            {
                device.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(volume / 100);
                device.AudioEndpointVolume.Mute = false;
            }

            e.Handled = true;
        }

        #endregion

        #region SMTC

        private object _SMTC;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private async void SetupSMTCAsync()
        {
            if (!IsEnabled)
            {
                return;
            }

            GlobalSystemMediaTransportControlsSessionManager SMTC;

            SMTC = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            SMTC.SessionsChanged += SMTC_SessionsChanged;
            _SMTC = SMTC;

            LoadSessionControls();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void DetachSMTC()
        {
            if (_SMTC is GlobalSystemMediaTransportControlsSessionManager SMTC)
            {
                SMTC.SessionsChanged -= SMTC_SessionsChanged;
                _SMTC = null;
            }

            ClearSessionControls();
            SecondaryContentVisible = false;
        }

        private void ClearSessionControls()
        {
            foreach (var child in sessionsPanel.SessionsStackPanel.Children)
            {
                var s = child as SessionControl;
                s?.DisposeSession();
            }

            sessionsPanel.SessionsStackPanel.Children.Clear();
            _SMTCAvail = false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void SMTC_SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(LoadSessionControls));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LoadSessionControls()
        {
            ClearSessionControls();

            if (!IsEnabled)
            {
                return;
            }

            if (_SMTC is GlobalSystemMediaTransportControlsSessionManager SMTC)
            {
                var sessions = SMTC.GetSessions();

                foreach (var session in sessions)
                {
                    sessionsPanel.SessionsStackPanel.Children.Add(new SessionControl
                    {
                        SMTCSession = session
                    });
                }

                if (sessionsPanel.SessionsStackPanel.Children.Count > 0)
                {
                    SecondaryContentVisible = isVolumeFlyout ? ShowGSMTCInVolumeFlyout : true;
                    _SMTCAvail = true;
                }
            }
        }
        #endregion

        #region SMTC Thumbnails

        public static ImageSource GetDefaultAudioThumbnail()
        {
            return new BitmapImage(PackUriHelper.GetAbsoluteUri("Assets/Images/DefaultAudioThumbnail.png"));
        }

        public static ImageSource GetDefaultImageThumbnail()
        {
            return new BitmapImage(PackUriHelper.GetAbsoluteUri("Assets/Images/DefaultImageThumbnail.png"));
        }

        public static ImageSource GetDefaultVideoThumbnail()
        {
            return new BitmapImage(PackUriHelper.GetAbsoluteUri("Assets/Images/DefaultVideoThumbnail.png"));
        }

        #endregion

        protected override void OnEnabled()
        {
            base.OnEnabled();

            AppDataHelper.AudioModuleEnabled = IsEnabled;

            if (!IsEnabled)
            {
                return;
            }

            client.DefaultDeviceChanged += Client_DefaultDeviceChanged;

            if (device != null)
            {
                device.AudioEndpointVolume.OnVolumeNotification += AudioEndpointVolume_OnVolumeNotification;
                PrimaryContent = volumeControl;
            }
            else { PrimaryContent = noDeviceMessageBlock; }

            PrimaryContentVisible = isVolumeFlyout ? true : ShowVolumeControlInGSMTCFlyout;

            if (_isinit)
            {
                try { SetupSMTCAsync(); } catch { }
            }
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            client.DefaultDeviceChanged -= Client_DefaultDeviceChanged;

            if (device != null)
            {
                device.AudioEndpointVolume.OnVolumeNotification -= AudioEndpointVolume_OnVolumeNotification;
            }
            PrimaryContent = null;
            PrimaryContentVisible = false;

            try { DetachSMTC(); } catch { }

            AppDataHelper.AudioModuleEnabled = IsEnabled;
        }
    }

    public class AudioDeviceNotificationClient : IMMNotificationClient
    {
        public event EventHandler<string> DefaultDeviceChanged;

        public void OnDefaultDeviceChanged(DataFlow dataFlow, Role deviceRole, string defaultDeviceId)
        {
            if (dataFlow == DataFlow.Render && deviceRole == Role.Multimedia)
            {
                DefaultDeviceChanged?.Invoke(this, defaultDeviceId);
            }
        }

        public void OnDeviceAdded(string deviceId)
        { }

        public void OnDeviceRemoved(string deviceId)
        { }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        { }

        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey)
        { }
    }
}