using System.Windows;

namespace FiestaLauncher.Views
{
    public partial class LoginDialog : Window
    {
        public LoginDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => txtUsername.Focus();
        }

        public string Username => txtUsername.Text.Trim();
        public string Password => txtPassword.Password;

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show(
                    "Username und Passwort sind erforderlich.",
                    "Launcher Login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
