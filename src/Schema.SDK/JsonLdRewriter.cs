using System.Text.Json.Nodes;

namespace Schema.SDK;

public static class JsonLdRewriter
{
    public static JsonNode Rewrite(JsonNode node, UrlResolver resolver)
    {
        switch (node)
        {
            case JsonObject obj:
                return RewriteObject(obj, resolver);

            case JsonArray arr:
                var newArr = new JsonArray();
                foreach (var item in arr)
                    newArr.Add(item == null ? null : Rewrite(item, resolver));
                return newArr;

            default:
                return node.DeepClone();
        }
    }

    private static JsonObject RewriteObject(JsonObject obj, UrlResolver resolver)
    {
        var newObj = new JsonObject();

        foreach (var kv in obj)
            if (kv.Key == "@context")
                newObj["@context"] = RewriteContext(kv.Value, resolver);
            else
                newObj[kv.Key] = kv.Value == null
                    ? null
                    : Rewrite(kv.Value, resolver);

        return newObj;
    }

    private static JsonNode RewriteContext(JsonNode? ctx, UrlResolver resolver)
    {
        if (ctx == null)
            return ctx!;

        // Case: string URL
        if (ctx is JsonValue val && val.TryGetValue<string>(out var url) && url.StartsWith("http"))
            return resolver.Resolve(url);

        // Case: array of contexts
        if (ctx is JsonArray arr)
        {
            var newArr = new JsonArray();

            foreach (var item in arr)
                if (item is JsonValue v && v.TryGetValue(out url) && url.StartsWith("http"))
                    newArr.Add(resolver.Resolve(url));
                else
                    newArr.Add(item == null ? null : item.DeepClone());

            return newArr;
        }

        // Case: already object
        return ctx.DeepClone();
    }
}
