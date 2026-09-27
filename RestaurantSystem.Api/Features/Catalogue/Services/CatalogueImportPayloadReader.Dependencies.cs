using System.Text.Json;
using RestaurantSystem.Api.Features.Catalogue.Dtos;

namespace RestaurantSystem.Api.Features.Catalogue.Services;

internal static partial class CatalogueImportPayloadReader
{
    public static IReadOnlyList<CataloguePayloadDependency> ReadMaterializationDependencies(
        CentralCatalogueTemplateRevision revision)
    {
        var dependencies = new List<CataloguePayloadDependency>();
        switch (revision.Type)
        {
            case "item":
                AddOptional(dependencies, ReadOptionalReference(revision.Payload, "category"), "category");
                AddReferences(dependencies, ReadReferences(revision.Payload, "optionSets"), "option-set");
                AddReferences(dependencies, ReadReferences(revision.Payload, "sideSets"), "option-set");
                break;
            case "option-set":
                var set = ReadOptionSet(revision);
                var optionType = set.Kind is "ingredient" or "sauce" ? "ingredient" : "item";
                AddReferences(dependencies, set.Options.Select(option => option.Reference), optionType);
                break;
            case "bundle":
                AddOptional(dependencies, ReadOptionalReference(revision.Payload, "standaloneOffer"), "item");
                foreach (var section in ReadBundleSections(revision))
                {
                    AddReferences(dependencies, section.Options.Select(option => option.Reference), "item");
                }
                break;
        }

        return dependencies;
    }

    private static void AddOptional(
        List<CataloguePayloadDependency> dependencies,
        CatalogueSourceReference? reference,
        string expectedTemplateType)
    {
        if (reference is not null)
        {
            dependencies.Add(new CataloguePayloadDependency(reference, expectedTemplateType));
        }
    }

    private static void AddReferences(
        List<CataloguePayloadDependency> dependencies,
        IEnumerable<CatalogueSourceReference> references,
        string expectedTemplateType)
    {
        dependencies.AddRange(references.Select(reference =>
            new CataloguePayloadDependency(reference, expectedTemplateType)));
    }

}
