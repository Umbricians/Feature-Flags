using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Umbraco.Cms.Core.Security;

namespace Umbricians.FeatureFlags.Demo.DemoFlags;

/// <summary>
/// A pretend shop page whose parts are switched by flags, shown inside the /flags demo page. It checks flags with
/// Microsoft's own &lt;feature&gt; tag helper, exactly as a site would. Demo only, and only when the Development settings
/// give the test members a password.
/// </summary>
public class DemoShopController : Controller
{
    private readonly IMemberManager _members;
    private readonly IOptions<DemoMembersOptions> _options;
    private readonly IFeatureManagerSnapshot _features;

    public DemoShopController(IMemberManager members, IOptions<DemoMembersOptions> options, IFeatureManagerSnapshot features)
    {
        _members = members;
        _options = options;
        _features = features;
    }

    private const decimal FullPrice = 149.00m;

    [HttpGet("flags/shop")]
    public async Task<IActionResult> Index(string? outline = null)
    {
        if (string.IsNullOrEmpty(_options.Value.Password))
        {
            return NotFound();
        }

        MemberIdentityUser? member = await _members.GetCurrentMemberAsync();

        // A flag checked in C#, not in the markup: it changes the price the server works out, and the view just shows
        // whatever price it is given.
        decimal price = FullPrice;
        if (await _features.IsEnabledAsync(DemoFlagsComposer.MemberDiscount))
        {
            price = Math.Round(FullPrice * 0.9m, 2);
        }

        return View("~/Views/DemoShop/Index.cshtml", new DemoShopModel(member?.Name, outline == "1", FullPrice, price));
    }
}

/// <summary>Who is looking at the shop, whether to outline the parts flags switch, and the price worked out for them.</summary>
public record DemoShopModel(string? MemberName, bool Outline, decimal FullPrice, decimal Price)
{
    public bool Discounted => Price < FullPrice;
}
