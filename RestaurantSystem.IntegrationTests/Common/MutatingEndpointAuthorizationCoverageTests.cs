using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using RestaurantSystem.Api.Common.Authorization;
using RestaurantSystem.Api.Common.Filters;
using RestaurantSystem.Api.Common.Modules;

namespace RestaurantSystem.IntegrationTests.Common;

/// <summary>
/// Asserts that every state-CHANGING endpoint says out loud who may call it, and that the set of
/// endpoints answering "anyone" is a reviewed list rather than a default.
///
/// <para>
/// This exists because of backend #413: <c>POST /api/Products</c> shipped with no authorization
/// attribute at all and was reachable unauthenticated on production, while every sibling write in
/// the same controller carried <c>[RequireAdmin]</c>. Nothing went red, and nothing could have —
/// <c>Program.cs</c> registers a bare <c>AddAuthorization()</c> with no <c>FallbackPolicy</c>, so
/// an unmarked action is OPEN, and the whole suite passes an endpoint anyone on the internet can
/// call. PaymentsController already carried a comment naming this exact hazard; a comment does not
/// fail a build.
/// </para>
///
/// <para>
/// <b>Two rules, because the first one alone is a rubber stamp.</b> "Explicitly marked" makes
/// silence impossible, which is #413's failure mode — but on its own it is satisfied by writing
/// <c>[AllowAnonymous]</c>, so it is a documentation rule, not a security one.
/// <see cref="AnonymousWrites"/> supplies the teeth: the anonymous set is enumerated here, and
/// ADDING to it is what goes red. Guest ordering, guest baskets and login genuinely are public, so
/// the answer cannot be "none" — it can be "these, and a reviewer signed each one".
/// </para>
///
/// <para>
/// <b>Scope limits, stated rather than assumed.</b> (a) <b>Reads are out.</b> Several are anonymous
/// by design — the public menu is the product. That is a claim about GETs this codebase already
/// contradicts: <c>ReservationQuickActionsController.QuickApprove</c> is a <c>[HttpGet]</c> that
/// dispatches <c>ConfirmReservationCommand</c>. Verb-shaped, not effect-shaped, is a limit of this
/// rule and not a statement that GETs are safe. (b) <b>Minimal APIs are invisible</b> to any
/// reflection over controllers — <c>Program.cs</c> maps <c>/api/health</c> and the version
/// endpoints that way. All are GET today; a future <c>MapPost</c> is outside this rule by
/// construction.
/// </para>
/// </summary>
public class MutatingEndpointAuthorizationCoverageTests
{
    private static readonly Assembly Api = typeof(RequireAdminAttribute).Assembly;

    private static readonly string[] MutatingVerbs = ["POST", "PUT", "PATCH", "DELETE"];

    /// <summary>
    /// The measured size of the write surface, 2026-09-04. A RATCHET, and its stated end is that it
    /// only ever goes up: it exists to catch the scan losing its corpus (a namespace move, a
    /// changed discovery predicate, the wrong assembly), which is the one failure that turns
    /// "no violations" into "nothing was looked at". The previous floor here was 50 against an
    /// actual 130 — three-fifths of the surface could vanish and the control still read healthy.
    /// Raise it when the API grows; never lower it without saying which endpoints went away.
    /// </summary>
    private const int KnownMutatingActionCount = 130;

    /// <summary>
    /// Every write reachable with no credentials at all, measured 2026-09-04. Each is a deliberate
    /// product decision — signing up, signing in, a guest building a basket, a guest placing an
    /// order, a guest paying for it, a guest booking a table. Adding a line here is the review
    /// checkpoint this file exists to create: it should be argued for in the PR that adds it.
    /// </summary>
    private static readonly string[] AnonymousWrites =
    [
        "AuthController.AppleLogin [POST]",
        "AuthController.ForgotPassword [POST]",
        "AuthController.GoogleLogin [POST]",
        "AuthController.Login [POST]",
        "AuthController.RefreshToken [POST]",
        "AuthController.ResetPassword [POST]",
        "AuthController.SendEmailVerification [POST]",
        "AuthController.VerifyEmail [POST]",
        "BasketChannelController.ClearOrderType [DELETE]",
        "BasketChannelController.SetOrderType [PUT]",
        "BasketController.AddToBasket [POST]",
        "BasketController.ApplyPromoCode [POST]",
        "BasketController.ClearBasket [DELETE]",
        "BasketController.RemoveFromBasket [DELETE]",
        "BasketController.RemovePromoCode [DELETE]",
        "BasketController.UpdateBasketItem [PUT]",
        "OrderEmailController.SendOrderConfirmationEmail [POST]",
        "OrdersController.CreateOrder [POST]",
        "OrdersController.CreateOrderFromBasket [POST]",
        "PaymentsController.CreateCheckoutSession [POST]",
        "ReservationsController.CreateReservation [POST]",
        "UserController.ConfirmDeletion [POST]",
        "UserController.RegisterCustomer [POST]",
        "UserGroupController.ValidateQRCode [POST]",
    ];

    /// <summary>
    /// MVC's own discovery rule, narrowed to what this test can reason about: a public, concrete
    /// <see cref="ControllerBase"/>. `IsPublic` matters — it is why the probe types at the bottom
    /// of this file cannot accidentally become routes in the test host.
    /// </summary>
    private static Type[] DiscoveredControllers(Assembly assembly) => assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && t.IsPublic && typeof(ControllerBase).IsAssignableFrom(t))
        .ToArray();

    /// <summary>
    /// MVC's own action rule: a public instance method declared by the controller itself, not
    /// <c>[NonAction]</c>, not a property accessor. Excluding the framework base types matters —
    /// <see cref="ControllerBase.Ok()"/> and its siblings are public instance methods too, and the
    /// verb test below treats a method with NO verb attribute as mutating.
    /// </summary>
    private static bool IsAction(MethodInfo m) =>
        !m.IsSpecialName
        && m.GetCustomAttribute<NonActionAttribute>() is null
        && m.DeclaringType != typeof(object)
        && m.DeclaringType != typeof(ControllerBase)
        && m.DeclaringType != typeof(Controller);

    /// <summary>
    /// Enumerated through <see cref="IActionHttpMethodProvider"/>, not <see
    /// cref="HttpMethodAttribute"/>: <c>[AcceptVerbs("POST")]</c> implements the interface DIRECTLY
    /// and does not derive from the attribute, so a verb-attribute scan routes past it exactly
    /// where MVC routes to it. An action with NO verb provider is treated as mutating, because
    /// under the controller-level <c>[Route]</c> it answers every verb, POST included. Neither
    /// shape exists in the assembly today; the point is that a rule meant to be unavoidable should
    /// not have a syntax that avoids it.
    /// </summary>
    private static MethodInfo[] MutatingActions(IEnumerable<Type> controllers) => controllers
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        .Where(IsAction)
        .Where(m =>
        {
            var verbs = m.GetCustomAttributes().OfType<IActionHttpMethodProvider>()
                .SelectMany(a => a.HttpMethods).ToArray();
            return verbs.Length == 0
                || verbs.Any(v => MutatingVerbs.Contains(v, StringComparer.OrdinalIgnoreCase));
        })
        .ToArray();

    /// <summary>
    /// The markers that count, NAMED rather than derived from an interface, because the obvious
    /// generalisation is wrong: <see cref="RequireModuleAttribute"/> is also an
    /// <c>IAuthorizationFilter</c>, so "any attribute in the authorization pipeline" would let a
    /// tenant-entitlement gate stand in for a caller check — and every action on the class-gated
    /// PaymentsController would read as marked with its role attributes deleted. A module gate
    /// answers "did this tenant buy the feature", never "may this caller do this".
    /// <see cref="A_module_gate_is_not_a_caller_check"/> pins that.
    /// <list type="bullet">
    /// <item><see cref="AuthorizeAttribute"/> — every <c>Require*</c> role attribute derives from
    /// it, so one check covers the ones this codebase has and the ones it will grow. Honoured at
    /// class level too: a class-level <c>[Authorize]</c> really does protect its actions.</item>
    /// <item><see cref="AllowAnonymousAttribute"/> — <b>on the action only</b>. See
    /// <see cref="Marked(MemberInfo)"/> for why class level is refused.</item>
    /// <item><see cref="ApiKeyAuthFilter"/> — the printer fleet's device key, the only thing in
    /// front of endpoints that have no user to authorize. It answers 401 to a wrong key. It also
    /// fails OPEN when the key is unconfigured (#475), so what this file certifies is that a
    /// caller check is ATTACHED, not that it is correctly provisioned.</item>
    /// </list>
    /// </summary>
    private static bool IsExplicitlyMarked(MethodInfo action) =>
        Marked(action) || MarkedAtClassLevel(action.DeclaringType!);

    private static bool Marked(MemberInfo target) =>
        target.GetCustomAttributes<AuthorizeAttribute>().Any()
        || target.GetCustomAttributes<AllowAnonymousAttribute>().Any()
        || target.GetCustomAttributes<ApiKeyAuthFilter>().Any();

    /// <summary>
    /// Deliberately NOT <see cref="Marked(MemberInfo)"/>. A class-level
    /// <see cref="AllowAnonymousAttribute"/> is the one attribute that both satisfies a
    /// "was it marked" question and DEFEATS the per-action role attributes underneath it — measured
    /// in ASP.NET Core: an action carrying <c>[Authorize(Roles="Admin")]</c> on a class carrying
    /// <c>[AllowAnonymous]</c> answers 200 to a customer. Accepting it here would let one line at
    /// the top of a controller certify every write in it, present and future, as authorized while
    /// silently disabling the roles they declare. Class-level <c>[Authorize]</c> and the device key
    /// have no such inversion, so they are honoured.
    /// </summary>
    private static bool MarkedAtClassLevel(Type controller) =>
        controller.GetCustomAttributes<AuthorizeAttribute>().Any()
        || controller.GetCustomAttributes<ApiKeyAuthFilter>().Any();

    private static string Describe(MethodInfo m) =>
        $"{m.DeclaringType!.Name}.{m.Name} [{string.Join('/', m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().SelectMany(a => a.HttpMethods))}]";

    [Fact]
    public void Every_mutating_endpoint_is_explicitly_authorized_or_explicitly_anonymous()
    {
        var unmarked = MutatingActions(DiscoveredControllers(Api))
            .Where(m => !IsExplicitlyMarked(m))
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        unmarked.Should().BeEmpty(
            "an unmarked write endpoint is OPEN — there is no FallbackPolicy behind it (backend #413). "
            + "Add the role attribute it needs, or [AllowAnonymous] if it is genuinely public");
    }

    /// <summary>
    /// The half with teeth. The rule above is satisfied by writing <c>[AllowAnonymous]</c>; this one
    /// is not, so a new public write has to be argued for in the diff that adds it.
    /// </summary>
    [Fact]
    public void The_set_of_writes_reachable_with_no_credentials_is_the_reviewed_one()
    {
        var anonymous = MutatingActions(DiscoveredControllers(Api))
            .Where(m => m.GetCustomAttributes<AllowAnonymousAttribute>().Any()
                        && !m.GetCustomAttributes<AuthorizeAttribute>().Any())
            .Select(Describe)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        anonymous.Should().BeEquivalentTo(AnonymousWrites,
            "a write that anyone on the internet can call is a product decision, not a default — "
            + "if this is red, either a new one appeared or a listed one was secured; update the list in the same PR");
    }

    /// <summary>
    /// The positive control for both rules above. An empty violation list is only good news if the
    /// scan actually reached the code — a rename, a moved namespace or a wrong assembly would
    /// otherwise report a clean sweep of nothing at all.
    /// </summary>
    [Fact]
    public void The_scan_reaches_the_real_controllers()
    {
        var actions = MutatingActions(DiscoveredControllers(Api)).Select(Describe).ToArray();

        actions.Should().HaveCountGreaterThanOrEqualTo(KnownMutatingActionCount,
            "the scan lost part of its corpus — a 'no violations' result would mean nothing was looked at");
        actions.Should().Contain(a => a.StartsWith("ProductsController.CreateProduct", StringComparison.Ordinal));
        actions.Should().Contain(a => a.StartsWith("ProductsController.UpdateProduct", StringComparison.Ordinal));
        actions.Should().Contain(a => a.StartsWith("OrdersController.CreateOrder", StringComparison.Ordinal));
    }

    /// <summary>
    /// The NEGATIVE control: proof the predicate can say "no". Without this, a bug that made
    /// <see cref="IsExplicitlyMarked"/> return true unconditionally would leave the whole file
    /// green forever while enforcing nothing — the exact failure mode #413 is an instance of.
    /// The probes are private nested types, so MVC's discovery (which requires a public type)
    /// cannot pick them up as real routes.
    /// </summary>
    [Fact]
    public void The_predicate_discriminates_a_marked_endpoint_from_an_unmarked_one()
    {
        Single<UnmarkedProbeController>().Should().Match<MethodInfo>(m => !IsExplicitlyMarked(m));
        Single<MarkedProbeController>().Should().Match<MethodInfo>(m => IsExplicitlyMarked(m));
        Single<AnonymousProbeController>().Should().Match<MethodInfo>(m => IsExplicitlyMarked(m));
    }

    /// <summary>
    /// The device key counts. This is the widening that lets <c>DevicesController</c>'s three POSTs
    /// out of the violation list, and a widening is not finished until something proves it did not
    /// over-match — which is what the two tests below are for.
    /// </summary>
    [Fact]
    public void A_device_api_key_filter_is_a_caller_check()
    {
        Single<ApiKeyProbeController>().Should().Match<MethodInfo>(m => IsExplicitlyMarked(m));
    }

    /// <summary>
    /// Over-match control 1. <see cref="RequireModuleAttribute"/> implements the same
    /// <c>IAuthorizationFilter</c> interface as <see cref="ApiKeyAuthFilter"/> and must NOT satisfy
    /// this rule: it refuses a tenant who did not buy the module and says nothing whatever about
    /// who is calling. If this goes green, the predicate has been generalised to the interface and
    /// the rule enforces much less than it reads as enforcing.
    /// </summary>
    [Fact]
    public void A_module_gate_is_not_a_caller_check()
    {
        Single<ModuleGatedProbeController>().Should().Match<MethodInfo>(m => !IsExplicitlyMarked(m));
    }

    /// <summary>
    /// Over-match control 2: a controller-level <c>[AllowAnonymous]</c> must not certify a bare
    /// action underneath it. One line at the top of a controller would otherwise mark every write
    /// in it, present and future, as a reviewed decision that nobody made.
    /// </summary>
    [Fact]
    public void A_class_level_anonymous_does_not_certify_a_bare_action_under_it()
    {
        Single<ClassAnonymousProbeController>().Should().Match<MethodInfo>(m => !IsExplicitlyMarked(m));
    }

    /// <summary>
    /// The other, sharper half of the same hazard — and one no marker predicate can express, so it
    /// is asserted directly against the assembly. A class-level <c>[AllowAnonymous]</c> does not
    /// merely fail to protect: it OVERRIDES the role attributes on the actions beneath it. Measured
    /// in ASP.NET Core: an action carrying <c>[Authorize(Roles="Admin")]</c> inside a class carrying
    /// <c>[AllowAnonymous]</c> answers 200 to a customer. Such an action reads as authorized in
    /// every review, in the source, and to <see cref="IsExplicitlyMarked"/> — and is open.
    /// <para>
    /// So the rule is stricter than "must be marked": a controller with any write in it may not
    /// carry class-level anonymity at all. Zero controllers do today
    /// (<c>ReservationQuickActionsController</c> carries it over GETs only), which is what makes
    /// this cheap to hold rather than a migration.
    /// </para>
    /// </summary>
    [Fact]
    public void No_controller_with_a_write_carries_class_level_anonymity()
    {
        var offenders = MutatingActions(DiscoveredControllers(Api))
            .Select(m => m.DeclaringType!)
            .Distinct()
            .Where(t => t.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        offenders.Should().BeEmpty(
            "a class-level [AllowAnonymous] silently overrides every role attribute on the actions "
            + "beneath it — put it on the actions that are genuinely public instead");
    }

    /// <summary>
    /// The discrimination control for the assertion above: proof it can find an offender at all.
    /// An assembly-wide "found nothing" is worth exactly as much as the evidence that the query
    /// would have spoken up.
    /// </summary>
    [Fact]
    public void The_class_level_anonymity_sweep_can_find_an_offender()
    {
        var offenders = MutatingActions([typeof(ClassAnonymousRoledProbeController)])
            .Select(m => m.DeclaringType!)
            .Where(t => t.GetCustomAttributes<AllowAnonymousAttribute>().Any())
            .ToArray();

        offenders.Should().ContainSingle().Which.Should().Be<ClassAnonymousRoledProbeController>();
    }

    /// <summary>
    /// A read must NOT satisfy the rule by accident — if the verb filter matched everything, the
    /// main assertion would be scanning GETs too and its "no violations" would be meaningless.
    /// </summary>
    [Fact]
    public void A_read_only_endpoint_is_not_treated_as_a_mutation()
    {
        MutatingActions([typeof(ReadOnlyProbeController)]).Should().BeEmpty();
    }

    /// <summary>
    /// Pins the verb enumeration to the interface rather than the attribute: <c>[AcceptVerbs]</c>
    /// is routable by MVC and invisible to a <c>HttpMethodAttribute</c> scan.
    /// </summary>
    [Fact]
    public void An_AcceptVerbs_write_is_still_seen_as_a_mutation()
    {
        Single<AcceptVerbsProbeController>().Should().Match<MethodInfo>(m => !IsExplicitlyMarked(m));
    }

    /// <summary>
    /// An action with no verb attribute answers every verb under the controller's route, POST
    /// included, so it must be in scope. It must also not drag <see cref="ControllerBase"/>'s own
    /// public helpers in with it — hence <see cref="IsAction"/>, and hence the count assertion here.
    /// </summary>
    [Fact]
    public void A_verbless_action_is_in_scope_without_sweeping_in_framework_members()
    {
        MutatingActions([typeof(VerblessProbeController)])
            .Select(m => m.Name).Should().BeEquivalentTo(["Probe"]);
    }

    private static MethodInfo Single<T>() => MutatingActions([typeof(T)]).Should().ContainSingle().Subject;

    private sealed class UnmarkedProbeController : ControllerBase
    {
        [HttpPost("unmarked-probe")]
        public OkResult Probe() => Ok();
    }

    private sealed class MarkedProbeController : ControllerBase
    {
        [HttpPost("marked-probe")]
        [RequireAdmin]
        public OkResult Probe() => Ok();
    }

    private sealed class AnonymousProbeController : ControllerBase
    {
        [HttpPost("anonymous-probe")]
        [AllowAnonymous]
        public OkResult Probe() => Ok();
    }

    private sealed class ReadOnlyProbeController : ControllerBase
    {
        [HttpGet("read-probe")]
        public OkResult Probe() => Ok();
    }

    private sealed class ApiKeyProbeController : ControllerBase
    {
        [HttpPost("api-key-probe")]
        [ApiKeyAuthFilter]
        public OkResult Probe() => Ok();
    }

    private sealed class ModuleGatedProbeController : ControllerBase
    {
        [HttpPost("module-gated-probe")]
        [RequireModule(ModuleIds.Cashier)]
        public OkResult Probe() => Ok();
    }

    [AllowAnonymous]
    private sealed class ClassAnonymousProbeController : ControllerBase
    {
        [HttpPost("class-anonymous-probe")]
        public OkResult Probe() => Ok();
    }

    /// <summary>The composition where "marked" and "protected" come apart: the action's
    /// <c>[RequireAdmin]</c> is inert under the class's <c>[AllowAnonymous]</c>.</summary>
    [AllowAnonymous]
    private sealed class ClassAnonymousRoledProbeController : ControllerBase
    {
        [HttpPost("class-anonymous-roled-probe")]
        [RequireAdmin]
        public OkResult Probe() => Ok();
    }

    private sealed class AcceptVerbsProbeController : ControllerBase
    {
        [AcceptVerbs("POST", "PUT", Route = "accept-verbs-probe")]
        public OkResult Probe() => Ok();
    }

    private sealed class VerblessProbeController : ControllerBase
    {
        public OkResult Probe() => Ok();
    }
}
