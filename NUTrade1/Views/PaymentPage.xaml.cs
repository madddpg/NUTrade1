using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class PaymentPage : AppContentPage
{
    public PaymentPage(PaymentViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
