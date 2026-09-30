using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class OrderPaymentPage : AppContentPage
{
    public OrderPaymentPage(OrderPaymentViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
