using System.Runtime.CompilerServices;

namespace SB.Framework.Core.Util;

/// <summary>
/// 重试工具：指数退避 + 抖动。用于网络重连、Webhook 投递、HTTP 调用、消息补偿等易失败操作。
/// </summary>
public static class RetryUtil
{
    /// <summary>
    /// 执行带返回值的可重试操作。
    /// </summary>
    /// <param name="action">操作（参数1=当前尝试次数，从1开始；参数2=取消令牌）</param>
    /// <param name="options">重试选项，null 用默认（3 次、1s 起步指数退避 + 20% 抖动）</param>
    /// <param name="cancellationToken">取消令牌</param>
    public static async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> action,
        RetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RetryOptions.Default;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= options.MaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await action(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt > options.MaxRetries) break;
                if (options.ShouldRetry != null && !options.ShouldRetry(ex, attempt)) break;

                var delay = ComputeDelay(options, attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// 执行无返回值的可重试操作。
    /// </summary>
    public static async Task ExecuteAsync(
        Func<int, CancellationToken, Task> action,
        RetryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= RetryOptions.Default;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= options.MaxRetries + 1; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action(attempt, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt > options.MaxRetries) break;
                if (options.ShouldRetry != null && !options.ShouldRetry(ex, attempt)) break;

                var delay = ComputeDelay(options, attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// 计算第 attempt 次重试的等待时长（指数退避 + 抖动）。
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static TimeSpan ComputeDelay(RetryOptions options, int attempt)
    {
        // attempt 从 1 起：第 1 次重试 = BaseDelay * 2^0 = BaseDelay
        double exponential = options.UseExponentialBackoff
            ? options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1)
            : options.BaseDelay.TotalMilliseconds;

        var ms = (long)Math.Min(exponential, options.MaxDelay.TotalMilliseconds);

        // 抖动：±JitterFactor
        if (options.JitterFactor > 0)
        {
            var jitter = ms * options.JitterFactor * (Random.Shared.NextDouble() * 2 - 1);
            ms = Math.Max(0, ms + (long)jitter);
        }

        return TimeSpan.FromMilliseconds(ms);
    }
}

/// <summary>
/// 重试选项。
/// </summary>
public class RetryOptions
{
    /// <summary>最大重试次数（不含首次执行）。默认 3。</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>初始退避（第 1 次重试的基准延迟）。默认 1 秒。</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>退避上限。默认 30 秒。</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>抖动因子（0~1），默认 0.2 = ±20%。</summary>
    public double JitterFactor { get; set; } = 0.2;

    /// <summary>是否指数退避（false=固定延迟）。默认 true。</summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// 判断某异常是否应重试（参数1=异常，参数2=即将进行的尝试次数）。
    /// null = 所有异常都重试。
    /// </summary>
    public Func<Exception, int, bool>? ShouldRetry { get; set; }

    /// <summary>默认配置。</summary>
    public static RetryOptions Default => new();
}
