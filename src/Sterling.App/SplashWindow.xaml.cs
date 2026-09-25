using System.Reflection;
using System.Windows;
namespace Sterling.App;
public partial class SplashWindow : Window
{
    public SplashWindow() { InitializeComponent(); SplashVersion.Text = "v" + Assembly.GetExecutingAssembly().GetName().Version!.ToString(3); }
    public void SetStatus(string status) => StartupStatus.Text = status;
}
