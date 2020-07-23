﻿using System;
using MvvmCross.Forms.Presenters.Attributes;
using MvvmCross.Forms.Views;
using Rocks.Wasabee.Mobile.Core.ViewModels;
using Xamarin.Forms;
using Xamarin.Forms.Xaml;
using MenuItem = Rocks.Wasabee.Mobile.Core.ViewModels.MenuItem;

namespace Rocks.Wasabee.Mobile.Core.Views
{
    [XamlCompilation(XamlCompilationOptions.Compile)]
    [MvxMasterDetailPagePresentation(MasterDetailPosition.Master)]
    public partial class MenuView : MvxContentPage<MenuViewModel>
    {
        public MenuView()
        {
            InitializeComponent();
            MenuList.ItemSelected += (sender, e) => { ((ListView)sender).SelectedItem = null; };
        }

        private void Logout_Clicked(object sender, EventArgs e)
        {
            CloseMenu();
            ViewModel.LogoutCommand.Execute();
        }

        private void MenuList_OnItemTapped(object sender, ItemTappedEventArgs e)
        {
            CloseMenu();

            if (!(e.Item is MenuItem menuItem)) return;

            if (menuItem.ViewModelType == null)
                return;

            ViewModel.SelectedMenuItem = menuItem;
        }

        private void CloseMenu()
        {
            if (Parent is MasterDetailPage md)
            {
                md.MasterBehavior = MasterBehavior.Popover;
                md.IsPresented = !md.IsPresented;
            }
        }
    }
}