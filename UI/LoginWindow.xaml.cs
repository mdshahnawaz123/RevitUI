using DataLab.LicFolder;
using System;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RevitUI.UI
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            // Clear previous error
            TB_Error.Text = "";
            TB_Error.Foreground = new SolidColorBrush(Colors.Red);

            // Basic empty field check before hitting network
            if (string.IsNullOrWhiteSpace(TB_Username.Text))
            {
                TB_Error.Text = "Please enter your username.";
                TB_Username.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(PB_Password.Password))
            {
                TB_Error.Text = "Please enter your password.";
                PB_Password.Focus();
                return;
            }

            // Disable UI and show loading state
            SetLoadingState(true);

            var ok = await LicenseManager.LoginAsync(
                TB_Username.Text.Trim(),
                PB_Password.Password,
                m =>
                {
                    TB_Error.Text = m;
                    TB_Error.Foreground = new SolidColorBrush(Colors.Red);
                });

            SetLoadingState(false);

            if (ok)
            {
                // Show trial days remaining as a friendly notice
                int days = LicenseManager.GetTrialDaysRemaining(TB_Username.Text.Trim());
                if (days >= 0 && days <= 30)
                {
                    // Show warning if 7 days or fewer remain
                    if (days <= 7)
                    {
                        TB_Error.Foreground = new SolidColorBrush(Colors.OrangeRed);
                        TB_Error.Text = days == 0
                            ? "Your trial expires today!"
                            : $"Warning: Only {days} trial day(s) remaining.";

                        // Give user a moment to read the warning before closing
                        await System.Threading.Tasks.Task.Delay(1800);
                    }
                }

                DialogResult = true;
                Close();
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            IsEnabled = !isLoading;

            if (isLoading)
            {
                TB_Error.Foreground = new SolidColorBrush(Colors.Gray);
                TB_Error.Text = "Signing in, please wait...";
                // If you have a loading spinner in XAML, show it here:
                // Spinner.Visibility = Visibility.Visible;
            }
            else
            {
                TB_Error.Text = "";
                // Spinner.Visibility = Visibility.Collapsed;
            }
        }
    }
}
