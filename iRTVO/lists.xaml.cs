﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
// additional
using System.Windows.Threading;
using System.ComponentModel;
using Ini;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace iRTVO
{
    /// <summary>
    /// Interaction logic for lists.xaml
    /// </summary>
    public partial class Lists : Window
    {
        iRacingAPI API;
        DispatcherTimer updateTimer = new DispatcherTimer();

        Thread replayThread;

        public Lists()
        {
            InitializeComponent();

            this.Left = Properties.Settings.Default.listsWindowLocationX;
            this.Top = Properties.Settings.Default.listsWindowLocationY;
            this.Width = Properties.Settings.Default.listsWindowWidth;
            this.Height = Properties.Settings.Default.listsWindowHeight;

            if (Properties.Settings.Default.AoTlists == true)
                this.Topmost = true;
            else
                this.Topmost = false;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            standingsGrid.DataContext = SharedData.Sessions.CurrentSession.Standings;
            //eventsGrid.DataContext = SharedData.Events.List;
            //BookmarksGrid.DataContext = SharedData.Bookmarks.List;

            API = new iRTVO.iRacingAPI();
            API.sdk.Startup();

            updateTimer.Interval = new TimeSpan(0, 0, 0, 0, 500);
            updateTimer.Tick += new EventHandler(updateGrids);
            updateTimer.Start();
            updateGrids(new object(), new EventArgs());

            int i = 0;

            foreach (Single sector in SharedData.Sectors)
            {
                CheckBox cb = new CheckBox();
                cb.Content = "Sector " + i + ": " + sector.ToString("0.000");
                cb.Name = "s" + i;
                cb.Click += new RoutedEventHandler(sectorClick);
                sectorsStackPanel.Children.Add(cb);

                int found = SharedData.SelectedSectors.FindIndex(s => s.Equals(sector));
                if (found >= 0)
                {
                    cb.IsChecked = true;
                }

                i++;
            }

        }

        // no focus
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            //Set the window style to noactivate.
            WindowInteropHelper helper = new WindowInteropHelper(this);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        void sectorClick(object sender, RoutedEventArgs e)
        {
            CheckBox fe2 = (CheckBox)e.Source;
            int index = Int32.Parse(fe2.Name.Substring(1, fe2.Name.Length - 1));

            if ((Boolean)fe2.IsChecked)
            {
                SharedData.SelectedSectors.Add(SharedData.Sectors[index]);
            }
            else
            {
                SharedData.SelectedSectors.Remove(SharedData.Sectors[index]);
            }

            SharedData.SelectedSectors.Sort();

            IniFile sectorsIni = new IniFile(Directory.GetCurrentDirectory() + "\\sectors.ini");
            sectorsIni.IniWriteValue("Sectors", SharedData.Track.id.ToString(), String.Join(";", SharedData.SelectedSectors));

        }
             
        private void Window_LocationChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.listsWindowLocationX = (int)this.Left;
            Properties.Settings.Default.listsWindowLocationY = (int)this.Top;
            Properties.Settings.Default.Save();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            Properties.Settings.Default.listsWindowWidth = (int)this.Width;
            Properties.Settings.Default.listsWindowHeight = (int)this.Height;
            Properties.Settings.Default.Save();
        }

        void standingsGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (standingsGrid.SelectedItem != null)
            {
                Sessions.SessionInfo.StandingsItem driver = (Sessions.SessionInfo.StandingsItem)standingsGrid.SelectedItem;
                //Console.WriteLine("NumPlate (str): " + driver.Driver.NumberPlate + " (int):" + Int32.Parse(driver.Driver.NumberPlate).ToString());
                API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.CamSwitchNum, Int32.Parse(driver.Driver.NumberPlate), -1);

                //Console.WriteLine("Pos (int):" + driver.Position);
                //API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.CamSwitchPos, driver.Position, 1, 0);
                SharedData.updateControls = true;
            }
        }

        void eventsGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (eventsGrid.SelectedItem != null)
            {
                Event ev = (Event)eventsGrid.SelectedItem;
                replayThread = new Thread(rewind);
                replayThread.Start(ev);
            }
        }

        void bookmarksGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (BookmarksGrid.SelectedItem != null)
            {
                Event ev = (Event)BookmarksGrid.SelectedItem;
                replayThread = new Thread(rewind);
                replayThread.Start(ev);
            }
        }

        public void rewind(Object input)
        {
            Event ev = (Event)input;

            SharedData.replayInProgress = true;
            SharedData.replayReady.Reset();
            SharedData.replayReady.WaitOne();

            API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.ReplaySetPlaySpeed, 0, 0);

            Thread.Sleep(Properties.Settings.Default.ReplayMinLength-500);

            API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.CamSwitchNum, Int32.Parse(ev.Driver.NumberPlate), -1);
            API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.ReplaySetPlayPosition, (int)iRSDKSharp.ReplayPositionModeTypes.Begin, (int)ev.ReplayPos);
            API.sdk.BroadcastMessage(iRSDKSharp.BroadcastMessageTypes.ReplaySetPlaySpeed, 1, 0);
            SharedData.updateControls = true;

            SharedData.replayInProgress = false;
        }

        private void updateGrids(object sender, EventArgs e)
        {
            int gridcount = BookmarksGrid.Items.Count;
            int bookmarkcount = SharedData.Bookmarks.List.Count;

            if (gridcount != bookmarkcount)
            {
                for (int i = gridcount; i < bookmarkcount; i++)
                {
                    BookmarksGrid.Items.Add(SharedData.Bookmarks.List[i]);
                }
            }

            gridcount = eventsGrid.Items.Count;
            int eventcount = SharedData.Events.List.Count;

            if (gridcount != eventcount)
            {
                for (int i = gridcount; i < eventcount; i++)
                {
                    eventsGrid.Items.Add(SharedData.Events.List[i]);
                }
            }
        }

        private void Rewind_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ComboBoxItem cbi = (ComboBoxItem)Rewind.SelectedItem;
            string secstr = cbi.Content.ToString();
            int secint = Int32.Parse(secstr.Substring(0, secstr.Length - 1));
            SharedData.replayRewind = secint;
        }
    }
}
