using CodexController.Views;

namespace CodexController.Tests;

public sealed class FullResetStatusViewTests
{
    [Fact]
    public void VisibilityFollowsWhetherCreditTextIsPresent()
    {
        WpfTestHost.Run(() =>
        {
            var view = new FullResetStatusView();

            Assert.False(view.HasCredits);
            Assert.Equal(System.Windows.Visibility.Collapsed, view.Visibility);

            view.Text =
                "Full reset · 2026-07-31 12:03:12 -07:00";

            Assert.True(view.HasCredits);
            Assert.Equal(System.Windows.Visibility.Visible, view.Visibility);

            view.Text = " ";

            Assert.False(view.HasCredits);
            Assert.Equal(System.Windows.Visibility.Collapsed, view.Visibility);
        });
    }
}
