using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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

    public DemoShopController(IMemberManager members, IOptions<DemoMembersOptions> options)
    {
        _members = members;
        _options = options;
    }

    [HttpGet("flags/shop")]
    public async Task<IActionResult> Index(string? outline = null)
    {
        if (string.IsNullOrEmpty(_options.Value.Password))
        {
            return NotFound();
        }

        MemberIdentityUser? member = await _members.GetCurrentMemberAsync();
        return View("~/Views/DemoShop/Index.cshtml", new DemoShopModel(member?.Name, outline == "1"));
    }
}

/// <summary>Who is looking at the shop, and whether to outline the parts flags switch.</summary>
public record DemoShopModel(string? MemberName, bool Outline);
