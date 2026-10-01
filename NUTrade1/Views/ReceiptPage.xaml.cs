using NUTrade1.ViewModels;

namespace NUTrade1.Views;

public partial class ReceiptPage : AppContentPage
{
    public ReceiptPage(ReceiptViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
