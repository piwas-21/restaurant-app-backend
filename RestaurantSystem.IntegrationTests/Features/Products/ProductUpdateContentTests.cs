using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantSystem.Api.Common.Validation;
using RestaurantSystem.Domain.Common.Enums;
using RestaurantSystem.Domain.Entities;
using RestaurantSystem.Infrastructure.Persistence;
using RestaurantSystem.IntegrationTests.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RestaurantSystem.IntegrationTests.Features.Products;

// Issue #193: the translation-writing half of `PUT /api/Products/{id}` had no integration coverage
// at all, which is why the dead duplicate-language-code check that used to sit in front of it could
// not simply be deleted — ProductsControllerTests covers no update path, and #300's suite drives
// this endpoint only for its menu-definition branch.
//
// So these tests pin the CONTRACT of that block, not the removal:
//
//   * a content map REPLACES every existing description (remove-then-add, not merge);
//   * an omitted `content` key is a no-op — the `?? new ProductDescriptionsDto()` coalesce;
//   * an empty `"content": {}` is also a no-op — the separate `if (contentMap.Any())` guard, which
//     is what stops "I did not touch translations" from meaning "delete all translations" (the
//     exact defect #190 fixed on the bundle side, where the guard was missing);
//   * a body carrying the SAME language code twice is accepted, and the last one wins;
//   * "fr" and "FR" are NOT the same key, and both are stored — the duplicate-ish shape that is
//     actually reachable, recorded so nobody blames the removal for it.
//
// That last one is the deadness proof the removal rests on, and it is deliberately written as a
// statement about observable behaviour rather than about the deleted lines: whatever the handler
// does or does not check, two "fr" entries must produce one French description holding the second
// entry's values. It passes identically before and after the deletion, which is the point — a
// regression test for a removal is only worth anything if it was green beforehand.
//
// Why the check was dead — argued from the TYPE, not from the transport, and the precise form of
// the argument matters because the loose form is false. A Dictionary<string, …> CAN hold two
// ordinally-equal keys when built with a finer comparer, and the deleted check grouped ordinally,
// so such a map would have made it fire. What rules that out is that ProductDescriptionsDto declares
// no constructor — C# does not inherit them, so the comparer-taking overload is not callable on it
// and every instance is ordinal (checked by reflection: one public ctor, no parameters). With the
// comparer pinned, the group-by is empty for every possible value of the map, including one built in
// C# rather than deserialized — so no converter, content type or future internal caller can revive
// the branch.
//
// Separately, on the wire and UNDER THE DEFAULT `AllowDuplicateProperties` (true on .NET 10),
// System.Text.Json collapses duplicate JSON keys through the indexer, last-wins, instead of
// throwing. That is a default, not a law: with it set false the same body raises JsonException.
// See the note on the last-wins test — it is the one test here that option would redden.
//
// Measured, not reasoned: the raw two-"fr" body below reaches the handler as a map of Count 2
// (fr + en) and answers 200 rather than "Duplicate language codes found: fr". Forcing the old branch
// to fire made it print that message with an EMPTY language list, which is the check reporting its
// own deadness.
[Collection("Database Lane 3")]
public class ProductUpdateContentTests : IntegrationTestBase
{
    private Guid _productId;
    private Guid _categoryId;

    public ProductUpdateContentTests(DatabaseFixture databaseFixture)
        : base(databaseFixture)
    {
    }

    protected override async Task SeedTestData()
    {
        await base.SeedTestData();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _categoryId = (await context.Categories.OrderBy(c => c.Name).FirstAsync()).Id;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Translated Product",
            BasePrice = 12m,
            Type = ProductType.MainItem,
            IsActive = true,
            IsAvailable = true,
            Ingredients = new List<string>(),
            Allergens = new List<string>(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        };
        _productId = product.Id;

        // Two seeded languages, so a replace can be told apart from a merge: the payloads below
        // send "en" and "de", and it is the SURVIVAL of the un-mentioned "fr" that distinguishes
        // them. A single seeded language could not.
        product.Descriptions.Add(new ProductDescription
        {
            Lang = "en",
            Name = "Seeded EN Name",
            Description = "Seeded EN Description",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });
        product.Descriptions.Add(new ProductDescription
        {
            Lang = "fr",
            Name = "Seeded FR Name",
            Description = "Seeded FR Description",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test"
        });

        context.Products.Add(product);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Every description of the product as (Lang, Name, Description), ordered by language so the
    /// assertions can use <c>Equal</c> and therefore also pin the COUNT — a merge that left a stale
    /// row behind would pass any per-row lookup.
    /// </summary>
    private async Task<List<(string Lang, string Name, string Description)>> ReadDescriptionsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.ProductDescriptions
            .Where(d => d.ProductId == _productId)
            .OrderBy(d => d.Lang)
            .Select(d => new ValueTuple<string, string, string>(d.Lang, d.Name, d.Description))
            .ToListAsync();
    }

    /// <summary>
    /// The name every payload below sends — deliberately different from the seeded "Translated
    /// Product", so that "the rename landed" is available as a POSITIVE signal that the handler
    /// actually reached and completed its write path.
    ///
    /// The no-op tests need that signal and cannot get it from their own subject matter. They assert
    /// HTTP 200 plus descriptions-unchanged, and BOTH of those hold trivially if the handler bails
    /// out before the content block ever runs: `ApiResponse.Failure` is returned through
    /// `return Ok(result)`, so "Product not found" and "One or more categories not found" are also
    /// 200s that leave every description exactly where it was. Without the rename assertion, a
    /// change that stopped the handler reaching the content block at all would leave them green.
    /// </summary>
    private const string RenamedProduct = "Renamed Product";

    private async Task<string> ReadProductNameAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await context.Products
            .Where(p => p.Id == _productId)
            .Select(p => p.Name)
            .SingleAsync();
    }

    /// <summary>The <c>errors</c> array of an ApiResponse body, JSON-decoded (#306).</summary>
    private static async Task<List<string>> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : new List<string>();
    }

    private Task<HttpResponseMessage> PutRawAsync(string json) =>
        Client.PutAsync(
            $"/api/Products/{_productId}",
            new StringContent(json, Encoding.UTF8, "application/json"));

    /// <summary>
    /// The product payload every test here sends, with <paramref name="contentFragment"/> spliced in
    /// verbatim. Raw JSON rather than an anonymous object for two reasons: a duplicate key cannot be
    /// expressed in C# at all, and the ABSENCE of the `content` key has to be visible in the test
    /// rather than being a property someone could helpfully add back.
    /// </summary>
    private string BuildPayload(string? contentFragment) => $$"""
    {
      "id": "{{_productId}}",
      "name": "{{RenamedProduct}}",
      "basePrice": 12,
      "isActive": true,
      "isAvailable": true,
      "isSpecial": false,
      "preparationTimeMinutes": 10,
      "type": "mainItem",
      "kitchenType": "none",
      "displayOrder": 0,
      "categoryIds": ["{{_categoryId}}"],
      "primaryCategoryId": "{{_categoryId}}"{{(contentFragment is null ? "" : ",\n  " + contentFragment)}}
    }
    """;

    // ---- The contract of the content block ----------------------------------------------------

    // A content map is a full REPLACE: "en" is rewritten, "de" is created, and the seeded "fr" —
    // which the payload never mentions — must be gone. Name AND description are asserted on each,
    // because they are two separate assignments off the same DTO and a swap between them is
    // invisible to a name-only check.
    [Fact]
    public async Task ContentMap_ReplacesEveryDescription()
    {
        AuthenticateAsAdmin();

        var json = BuildPayload("""
        "content": {
            "en": { "name": "New EN Name", "description": "New EN Description" },
            "de": { "name": "New DE Name", "description": "New DE Description" }
          }
        """);

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadDescriptionsAsync()).Should().Equal(
            ("de", "New DE Name", "New DE Description"),
            ("en", "New EN Name", "New EN Description"));
    }

    // An edit that does not touch translations. The key is absent entirely, so `command.Content` is
    // null and the coalesce produces an empty map — which the `Any()` guard must then read as "do
    // not remove anything". Without that guard this is a silent wipe of every translation, which is
    // precisely what the bundle handler did before #190.
    [Fact]
    public async Task OmittedContentKey_LeavesExistingDescriptionsUntouched()
    {
        AuthenticateAsAdmin();

        var json = BuildPayload(null);
        json.Should().NotContain("content", "the payload must actually omit the key");

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The rename first: see RenamedProduct for why "unchanged descriptions" alone is not
        // evidence the content block ran.
        (await ReadProductNameAsync()).Should().Be(RenamedProduct);
        (await ReadDescriptionsAsync()).Should().Equal(
            ("en", "Seeded EN Name", "Seeded EN Description"),
            ("fr", "Seeded FR Name", "Seeded FR Description"));
    }

    // The same outcome from a DIFFERENT input, and worth its own test: an omitted key and an empty
    // object reach the guard by two different routes (the null coalesce vs. a genuinely empty map),
    // so a change that broke only one of them would still leave the other green.
    [Fact]
    public async Task EmptyContentObject_LeavesExistingDescriptionsUntouched()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload("\"content\": {}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProductNameAsync()).Should().Be(RenamedProduct);
        (await ReadDescriptionsAsync()).Should().Equal(
            ("en", "Seeded EN Name", "Seeded EN Description"),
            ("fr", "Seeded FR Name", "Seeded FR Description"));
    }

    // The deadness proof for #193's duplicate-language-code check, stated as behaviour: a body with
    // two "fr" entries is ACCEPTED — not refused with "Duplicate language codes found: fr" — and the
    // second entry is the one that survives.
    //
    // IF THIS TEST GOES RED WITH A 400/500 AND NOBODY TOUCHED THE HANDLER, look for
    // `AllowDuplicateProperties = false` having been added to the JSON options in Program.cs. It
    // defaults to true on .NET 10 and is what lets a two-"fr" body reach the handler at all; set
    // false, System.Text.Json raises JsonException at bind time and this body never arrives. That is
    // a legitimate hardening choice and does NOT resurrect the deleted check — it makes it more
    // unreachable — so the right response is to re-point this test at the new refusal, not to
    // restore the guard.
    //
    // "Exactly one fr row" is a JOINT product of two things, and it is worth being precise about
    // which, because an earlier draft of this comment claimed the assertion was un-mutatable and was
    // wrong. The collapse of the two JSON keys into one map entry happens in System.Text.Json,
    // upstream of the handler and beyond reach of any mutation here. But the seeded "fr" row is
    // cleared by the handler's own RemoveRange — delete that and this test sees the seeded row
    // alongside the newly written one and fails on both count and content. So the replace semantics
    // are load-bearing for this test too, and were mutation-checked as such.
    [Fact]
    public async Task DuplicateLanguageCodes_AreAcceptedAndLastOneWins()
    {
        AuthenticateAsAdmin();

        var json = BuildPayload("""
        "content": {
            "fr": { "name": "First FR Name", "description": "First FR Description" },
            "fr": { "name": "Second FR Name", "description": "Second FR Description" },
            "en": { "name": "New EN Name", "description": "New EN Description" }
          }
        """);

        var response = await PutRawAsync(json);
        var body = await response.Content.ReadAsStringAsync();

        // Asserted on the BODY as well as the status, because the failure branch this proves
        // unreachable returns ApiResponse.Failure — and the controller wraps that in a 200
        // (ApiResponse.Failure leaves the reason in errors[0]), so a status check alone would read
        // as green against a check that fired.
        body.Should().NotContain("Duplicate language codes",
            "the duplicate-key body must be accepted, not refused");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadDescriptionsAsync()).Should().Equal(
            ("en", "New EN Name", "New EN Description"),
            ("fr", "Second FR Name", "Second FR Description"));
    }

    // The reachable cousin, and the reason this suite does not stop at the test above.
    //
    // Two literal "fr" keys cannot survive into the map, which is what made the deleted check dead.
    // But "fr" and "FR" are DIFFERENT keys to a Dictionary<string, …> with the default ordinal
    // comparer, so they both survive — and the product ends up with two rows for one human language.
    //
    // Recorded here as a characterization test, deliberately asserting what the system DOES rather
    // than what it arguably should, because the important fact is about attribution: the deleted
    // guard grouped keys with that same ordinal comparer, so it would NOT have caught this either.
    // Removing it changes nothing about this behaviour. Without this test, the first person to hit a
    // doubled language after the removal has an obvious and wrong suspect.
    //
    // Measured: status 200, rows FR|UP and fr|low, and BOTH echoed back in the response's content
    // map. Whether that should be rejected or normalised is a product question, not part of #193 —
    // see #306.
    [Fact]
    public async Task CaseVariantLanguageCodes_AreBothStored_AndTheRemovedCheckWouldNotHaveCaughtThem()
    {
        AuthenticateAsAdmin();

        var json = BuildPayload("""
        "content": {
            "fr": { "name": "lower FR", "description": "lower FR Description" },
            "FR": { "name": "UPPER FR", "description": "UPPER FR Description" }
          }
        """);

        var response = await PutRawAsync(json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProductNameAsync()).Should().Be(RenamedProduct);

        // BeEquivalentTo, not Equal: this is the one assertion in the suite whose ORDER would be
        // decided by the database's collation rather than by anything the code does — "FR" sorts
        // before "fr" under C/ordinal but not under every locale collation, and ReadDescriptionsAsync
        // orders in SQL. Every other test here compares distinct lowercase codes, which collate
        // identically everywhere. Count and contents are still pinned exactly; only the row order is
        // left to the database, which is the only part that was never ours to assert.
        (await ReadDescriptionsAsync()).Should().BeEquivalentTo(new[]
        {
            ("FR", "UPPER FR", "UPPER FR Description"),
            ("fr", "lower FR", "lower FR Description")
        });
    }

    // ---- #306: the content map is now validated -----------------------------------------------

    // The handler writes this map straight into required, non-nullable columns, and NOTHING checked
    // it. Every shape below was measured through this endpoint before the rule existed:
    //
    //   missing "description"  -> 500        missing "name"      -> 500
    //   "en": null             -> 500        "": {...}           -> 200, junk row with Lang = ''
    //                                        "   ": {...}        -> 200, junk row with Lang = '   '
    //
    // The 500s violate §5.4 (user-facing errors are BadRequestException). They were ATOMIC — the
    // single SaveChangesAsync is what threw, after the RemoveRange was staged but before it was
    // committed — so nothing was ever lost, which is why `descriptions unchanged` is asserted on
    // every case rather than treated as the point. The blank-key cases are the opposite failure:
    // accepted silently, and the ONLY ones that actually wrote anything.
    //
    // The two seeded rows are the control. BOTH must survive every rejection, and for the blank-key
    // cases that also proves the write was refused BEFORE the RemoveRange rather than after — a fix
    // that validated too late would leave the product with no translations at all.
    [Theory]
    [InlineData("""
        "content": { "en": { "name": "Only Name" } }
        """, "A translation's description is required ('en')")]
    [InlineData("""
        "content": { "en": { "description": "Only Description" } }
        """, "A translation's name is required ('en')")]
    [InlineData("""
        "content": { "en": null }
        """, "A translation entry cannot be null ('en')")]
    [InlineData("""
        "content": { "": { "name": "blank", "description": "d" } }
        """, "A translation's language code is required")]
    [InlineData("""
        "content": { "   ": { "name": "whitespace", "description": "d" } }
        """, "A translation's language code is required")]
    public async Task MalformedContentEntry_IsRefusedAsABadRequest_AndChangesNothing(
        string contentFragment, string expectedMessage)
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(contentFragment));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Read the errors array rather than substring-matching the raw body: System.Text.Json escapes
        // the apostrophes in these messages, so a naive Contain() on the response string fails against
        // a response that is actually correct.
        //
        // ContainMatch, not exact element equality: ValidationBehavior joins every failure into ONE
        // string with "; ", so an exact match only holds while these payloads trip exactly one rule.
        // Any rule added to this validator later that also fires here would turn all five red for a
        // reason that has nothing to do with what they test.
        (await ReadErrorsAsync(response)).Should().ContainMatch($"*{expectedMessage}*");

        // The rename is the signal that the handler was refused OUTRIGHT rather than partially run.
        // Every payload here sends RenamedProduct, so a rule that fired too late — after the
        // product's own fields were written — would leave this changed.
        (await ReadProductNameAsync()).Should().Be("Translated Product");

        (await ReadDescriptionsAsync()).Should().Equal(
            ("en", "Seeded EN Name", "Seeded EN Description"),
            ("fr", "Seeded FR Name", "Seeded FR Description"));
    }

    // The counterpart the rule must NOT catch. An empty DESCRIPTION is a legitimate translation —
    // the admin form posts `description: data.description || ''` on every save, so a product with
    // no description text sends "" rather than omitting the key. Only null is refused there, and
    // this is what says so; tightening Description to NotEmpty would 400 an ordinary edit.
    //
    // This test used to send an empty NAME as well, and that half moved to
    // BlankName_IsRefusedAsABadRequest_AndChangesNothing below when #325 made a blank name a 400.
    // The two halves are split rather than merged so this one keeps stating the permissive rule
    // about Description on its own — that rule is unchanged and must stay pinned by a test that
    // cannot be reddened by a decision about Name.
    [Fact]
    public async Task EmptyButPresentDescription_IsAccepted()
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload("""
        "content": { "en": { "name": "New EN Name", "description": "" } }
        """));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadProductNameAsync()).Should().Be(RenamedProduct);
        // A full REPLACE, so the seeded "fr" is gone — the same contract ContentMap_ReplacesEveryDescription
        // pins. The point here is only that an empty description was ACCEPTED rather than refused.
        (await ReadDescriptionsAsync()).Should().Equal(("en", "New EN Name", ""));
    }

    // #325. The row this refusal exists to stop is the one the editor cannot re-save: a top-level
    // translation whose Name is blank or whitespace-only stores fine (200, measured), the admin
    // editor's payload builder then omits it from the NEXT save — `productFormUtils.ts` filters on
    // `e?.name?.trim()` — and `if (contentMap.Any()) RemoveRange(...)` deletes it, DESCRIPTION TEXT
    // INCLUDED. So the write is accepted and the data is destroyed later by an unrelated save.
    //
    // Whitespace and empty are two InlineData rows, not one: `"   "` is what `contentSchema.name`'s
    // `min(1)` counts as three valid characters, and `""` is what a client that sends the key with
    // no value produces. A guard written as `== string.Empty` passes the second and fails the first.
    //
    // Asserted on the ROWS, not only on the status: the two seeded translations must both survive,
    // which is what proves the refusal happened BEFORE the RemoveRange rather than after it.
    [Theory]
    [InlineData("""
        "content": { "en": { "name": "   ", "description": "Une pizza" } }
        """)]
    [InlineData("""
        "content": { "en": { "name": "", "description": "Une pizza" } }
        """)]
    public async Task BlankName_IsRefusedAsABadRequest_AndChangesNothing(string contentFragment)
    {
        AuthenticateAsAdmin();

        var response = await PutRawAsync(BuildPayload(contentFragment));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorsAsync(response)).Should()
            .ContainMatch($"*{ProductContentRule.NameRequiredMessage} ('en')*");

        (await ReadProductNameAsync()).Should().Be("Translated Product");
        (await ReadDescriptionsAsync()).Should().Equal(
            ("en", "Seeded EN Name", "Seeded EN Description"),
            ("fr", "Seeded FR Name", "Seeded FR Description"));
    }
}
