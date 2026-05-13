using System.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using SB.Framework.Models.Qdrant;

namespace SB.Framework.Services.Qdrant;

/// <summary>
/// 基于官方 <see cref="QdrantClient"/>(gRPC)的薄封装。建议作为 Singleton 注入。
/// </summary>
public sealed class QdrantDriver : IQdrantDriver, IDisposable
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantDriver> _logger;

    public QdrantDriver(IOptions<QdrantOptions> options, ILogger<QdrantDriver> logger)
    {
        var o = options.Value;
        _logger = logger;
        _client = new QdrantClient(
            host: o.Host,
            port: o.Port,
            https: o.UseHttps,
            apiKey: string.IsNullOrWhiteSpace(o.ApiKey) ? null : o.ApiKey);
    }

    // ===== 健康检查 =====

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            await _client.ListCollectionsAsync(ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant Ping 失败");
            return false;
        }
    }

    // ===== Collection 管理 =====

    public Task<bool> CollectionExistsAsync(string collection, CancellationToken ct = default)
        => _client.CollectionExistsAsync(collection, ct);

    public async Task EnsureCollectionAsync(
        string collection,
        ulong vectorSize,
        QdrantDistance distance = QdrantDistance.Cosine,
        CancellationToken ct = default)
    {
        if (await _client.CollectionExistsAsync(collection, ct))
            return;

        await _client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = vectorSize, Distance = ToGrpcDistance(distance) },
            cancellationToken: ct);
    }

    public Task DeleteCollectionAsync(string collection, CancellationToken ct = default)
        => _client.DeleteCollectionAsync(collection, cancellationToken: ct);

    public async Task<IReadOnlyList<string>> ListCollectionsAsync(CancellationToken ct = default)
    {
        var list = await _client.ListCollectionsAsync(ct);
        return list.ToList();
    }

    // ===== Points =====

    public Task UpsertAsync(string collection, IEnumerable<VectorPoint> points, CancellationToken ct = default)
    {
        var structs = points.Select(ToPointStruct).ToList();
        if (structs.Count == 0) return Task.CompletedTask;
        return _client.UpsertAsync(collection, structs, cancellationToken: ct);
    }

    public Task DeleteAsync(string collection, IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids as IReadOnlyList<Guid> ?? ids.ToList();
        if (idList.Count == 0) return Task.CompletedTask;
        return _client.DeleteAsync(collection, idList, cancellationToken: ct);
    }

    public Task DeleteAsync(string collection, Filter filter, CancellationToken ct = default)
        => _client.DeleteAsync(collection, filter, cancellationToken: ct);

    // ===== Search =====

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string collection,
        ReadOnlyMemory<float> queryVector,
        ulong limit = 10,
        Filter? filter = null,
        float? scoreThreshold = null,
        CancellationToken ct = default)
    {
        var result = await _client.SearchAsync(
            collectionName: collection,
            vector: queryVector,
            filter: filter,
            limit: limit,
            payloadSelector: true,
            scoreThreshold: scoreThreshold,
            cancellationToken: ct);

        var hits = new List<SearchHit>(result.Count);
        foreach (var p in result)
        {
            if (!TryGetGuid(p.Id, out var guid))
                continue; // 非 Guid 形态的 Id 不属于本驱动写入,跳过
            hits.Add(new SearchHit(guid, p.Score, ToDict(p.Payload)));
        }
        return hits;
    }

    public void Dispose() => _client.Dispose();

    // ===== 内部转换 =====

    private static Distance ToGrpcDistance(QdrantDistance d) => d switch
    {
        QdrantDistance.Cosine => Distance.Cosine,
        QdrantDistance.Euclid => Distance.Euclid,
        QdrantDistance.Dot => Distance.Dot,
        QdrantDistance.Manhattan => Distance.Manhattan,
        _ => Distance.Cosine,
    };

    private static PointStruct ToPointStruct(VectorPoint p)
    {
        var ps = new PointStruct
        {
            Id = new PointId { Uuid = p.Id.ToString("D") },
            Vectors = p.Vector.ToArray(),
        };
        if (p.Payload != null)
        {
            foreach (var kv in p.Payload)
                ps.Payload[kv.Key] = ToValue(kv.Value);
        }
        return ps;
    }

    private static bool TryGetGuid(PointId pid, out Guid guid)
    {
        if (pid.HasUuid && Guid.TryParse(pid.Uuid, out guid))
            return true;
        guid = default;
        return false;
    }

    private static Value ToValue(object? raw)
    {
        switch (raw)
        {
            case null:
                return new Value { NullValue = NullValue.NullValue };
            case bool b:
                return new Value { BoolValue = b };
            case string s:
                return new Value { StringValue = s };
            case Guid g:
                return new Value { StringValue = g.ToString("D") };
            case DateTime dt:
                return new Value { StringValue = dt.ToUniversalTime().ToString("O") };
            case DateTimeOffset dto:
                return new Value { StringValue = dto.ToString("O") };
            case float f:
                return new Value { DoubleValue = f };
            case double d:
                return new Value { DoubleValue = d };
            case decimal m:
                return new Value { DoubleValue = (double)m };
            case sbyte or byte or short or ushort or int or uint or long:
                return new Value { IntegerValue = Convert.ToInt64(raw) };
            case ulong ul:
                return new Value { IntegerValue = (long)ul };
            case IDictionary<string, object?> dict:
            {
                var s2 = new Struct();
                foreach (var kv in dict)
                    s2.Fields[kv.Key] = ToValue(kv.Value);
                return new Value { StructValue = s2 };
            }
            case IReadOnlyDictionary<string, object?> roDict:
            {
                var s2 = new Struct();
                foreach (var kv in roDict)
                    s2.Fields[kv.Key] = ToValue(kv.Value);
                return new Value { StructValue = s2 };
            }
            case IEnumerable seq:
            {
                var lv = new ListValue();
                foreach (var item in seq)
                    lv.Values.Add(ToValue(item));
                return new Value { ListValue = lv };
            }
            default:
                return new Value { StringValue = raw.ToString() ?? "" };
        }
    }

    private static IReadOnlyDictionary<string, object?> ToDict(Google.Protobuf.Collections.MapField<string, Value> payload)
    {
        var dict = new Dictionary<string, object?>(payload.Count);
        foreach (var kv in payload)
            dict[kv.Key] = FromValue(kv.Value);
        return dict;
    }

    private static object? FromValue(Value v) => v.KindCase switch
    {
        Value.KindOneofCase.NullValue => null,
        Value.KindOneofCase.BoolValue => v.BoolValue,
        Value.KindOneofCase.IntegerValue => v.IntegerValue,
        Value.KindOneofCase.DoubleValue => v.DoubleValue,
        Value.KindOneofCase.StringValue => v.StringValue,
        Value.KindOneofCase.ListValue => v.ListValue.Values.Select(FromValue).ToList(),
        Value.KindOneofCase.StructValue => v.StructValue.Fields.ToDictionary(kv => kv.Key, kv => FromValue(kv.Value)),
        _ => null,
    };
}
