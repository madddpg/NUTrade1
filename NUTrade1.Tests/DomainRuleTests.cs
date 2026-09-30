using NUTrade1.Core;
using Xunit;

namespace NUTrade1.Tests;

public class DomainRuleTests
{
    // Registration is open to any personal email — the campus-domain restriction is gone.
    // Ownership of the address is proven by the emailed one-time code, and being a real
    // NU – Lipa student by the Student ID photo an admin reviews. So all this needs to
    // catch is a malformed address before the sign-up request leaves the device.
    [Theory]
    [InlineData("juan.delacruz@nu-lipa.edu.ph", true)]
    [InlineData("JUAN.DELACRUZ@NU-LIPA.EDU.PH", true)]
    [InlineData("  maria@nu-lipa.edu.ph  ", true)]
    [InlineData("someone@gmail.com", true)]
    [InlineData("student@yahoo.com.ph", true)]
    [InlineData("no-at-sign.com", false)]
    [InlineData("two@at@signs.com", false)]
    [InlineData("@nodomain.com", false)]
    [InlineData("missing@tld", false)]
    [InlineData("trailing@dot.", false)]
    [InlineData("has space@mail.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidEmail_accepts_any_well_formed_address(string? email, bool expected)
    {
        Assert.Equal(expected, NUTradeConstants.IsValidEmail(email));
    }

    [Theory]
    [InlineData(ListingPackage.Free, 0)]
    [InlineData(ListingPackage.Additional, 1000)]
    [InlineData(ListingPackage.Priority, 2000)]
    public void FeeForPackage_matches_the_published_price_list(ListingPackage package, long expectedCentavos)
    {
        Assert.Equal(expectedCentavos, NUTradeConstants.FeeForPackage(package));
    }

    // The Free package is a student's first listing, once ever. Anything submitted and
    // not turned down uses it; a rejection hands it back. Must match
    // FREE_POST_USED_STATUSES in functions/src/constants.ts, which createQrPayment enforces.
    [Theory]
    [InlineData(ListingStatus.Draft, false)]
    [InlineData(ListingStatus.PendingPayment, false)]
    [InlineData(ListingStatus.PendingApproval, true)]
    [InlineData(ListingStatus.Active, true)]
    [InlineData(ListingStatus.Matched, true)]
    [InlineData(ListingStatus.Completed, true)]
    [InlineData(ListingStatus.Expired, true)]
    [InlineData(ListingStatus.Cancelled, false)]
    [InlineData(ListingStatus.Rejected, false)]
    public void UsesFreePost_counts_every_submitted_listing_except_rejected(ListingStatus status, bool expected)
    {
        Assert.Equal(expected, NUTradeConstants.UsesFreePost(status));
    }
}
