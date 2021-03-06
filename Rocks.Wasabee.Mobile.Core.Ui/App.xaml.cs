using MvvmCross.Forms.Core;

namespace Rocks.Wasabee.Mobile.Core.Ui
{
    public partial class App : MvxFormsApplication
    {
        public static App Instance;

        public App()
        {
            Instance = this;
            InitializeComponent();

            Plugin.Iconize.Iconize.With(new Plugin.Iconize.Fonts.MaterialDesignIconsModule());
        }

        protected override void OnStart()
        {
            base.OnStart();
        }

        protected override void OnSleep()
        {
            base.OnSleep();
        }

        protected override void OnResume()
        {
            base.OnResume();
        }
    }
}
