﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading;
using Materia.UI.Components;

namespace Materia.UI
{
    /// <summary>
    /// Interaction logic for UIPopupShelf.xaml
    /// </summary>
    public partial class UIPopupShelf : Window
    {
        CancellationTokenSource ctk;
        public UIGraph Graph { get; set; }

        List<NodeResource> clones;

        public UIPopupShelf()
        {
            InitializeComponent();
            clones = new List<NodeResource>();
        }

        private void PopulateView(string path)
        {
            var items = ShelfItem.Find(path);
            clones = new List<NodeResource>();
            ResourcesList.Items.Clear();

            foreach (var item in items)
            {
                clones.Add(item.Clone());
            }
            
            foreach(var item in clones)
            {
                item.MouseDown += Item_MouseDown;
                ResourcesList.Items.Add(item);
            }
        }

        private void Item_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                NodeResource src = sender as NodeResource;
                if (Graph != null)
                {
                    Graph.Insert(src.Type);

                    Keyboard.ClearFocus();
                    Graph.Focus();
                }

                Hide();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string s = SearchBox.Text;

            if (!IsLoaded) return;

            if (string.IsNullOrEmpty(s) || string.IsNullOrWhiteSpace(s) || s.Equals("Search..."))
            {
                ClearFilters();
                return;
            }

            if (ctk != null)
            {
                ctk.Cancel();
            }

            ctk = new CancellationTokenSource();

            Task.Delay(250, ctk.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;

                App.Current.Dispatcher.Invoke(() =>
                {
                    SetFilters(s);
                });
            });
        }

        private void SetFilters(string s)
        {
            s = s.ToLower();
            PopulateView(s);
        }

        private void ClearFilters()
        {
            PopulateView("Categories");
        }

        private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
        {
            string s = SearchBox.Text;

            if (s.Equals("Search..."))
            {
                SearchBox.Text = "";
            }
        }

        private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
        {
            string s = SearchBox.Text;

            if (string.IsNullOrWhiteSpace(s) || string.IsNullOrWhiteSpace(s))
            {
                SearchBox.Text = "Search...";
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (clones.Count == 0 && SearchBox.Text.Equals("Search..."))
            {
                PopulateView("Categories");
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if(Graph != null)
            {
                Keyboard.ClearFocus();
                Graph.Focus();
            }

            Hide();
        }
    }
}
