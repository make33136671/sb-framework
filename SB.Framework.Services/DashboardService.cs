using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class DashboardService
    {
        private readonly IFreeSql _fsql;

        public DashboardService(IFreeSql fsql)
        {
            _fsql = fsql;
        }

        /// <summary>
        /// 获取仪表盘概览数据
        /// </summary>
        public virtual Result GetOverview()
        {
            var today = AppTime.Today;
            var yesterday = today.AddDays(-1);

            // 会员统计
            var totalMembers = _fsql.Select<Member>().Count();
            var todayNewMembers = _fsql.Select<Member>().Where(m => m.CreatedTime >= today).Count();
            var yesterdayNewMembers = _fsql.Select<Member>()
                .Where(m => m.CreatedTime >= yesterday && m.CreatedTime < today).Count();

            // 今日登录（去重）
            var todayLogins = _fsql.Select<LoginLog>()
                .Where(l => l.CreateTime >= today && l.UserType == 0 && l.Status == 0)
                .Distinct()
                .ToList(l => l.UserId)
                .Count;
            var yesterdayLogins = _fsql.Select<LoginLog>()
                .Where(l => l.CreateTime >= yesterday && l.CreateTime < today && l.UserType == 0 && l.Status == 0)
                .Distinct()
                .ToList(l => l.UserId)
                .Count;

            // 实名认证统计
            var verifiedCount = _fsql.Select<Member>().Where(m => m.RealNameStatus == RealNameStatus.Verified).Count();
            var pendingReviewCount = _fsql.Select<MemberRealName>().Where(r => r.Status == RealNameStatus.Reviewing).Count();

            // 封禁会员
            var bannedCount = _fsql.Select<Member>().Where(m => m.Status == UserStatus.Banned).Count();

            return Result.Success(new
            {
                totalMembers,
                todayNewMembers,
                yesterdayNewMembers,
                todayLogins,
                yesterdayLogins,
                verifiedCount,
                pendingReviewCount,
                bannedCount,
            });
        }

        /// <summary>
        /// 获取最近N天的会员注册趋势
        /// </summary>
        public virtual Result GetMemberTrend(int days = 30)
        {
            if (days < 1) days = 30;
            if (days > 365) days = 365;

            var startDate = AppTime.Today.AddDays(-days + 1);

            var rawData = _fsql.Select<Member>()
                .Where(m => m.CreatedTime >= startDate)
                .ToList(m => new { m.CreatedTime });

            var grouped = rawData
                .GroupBy(m => m.CreatedTime.Date)
                .ToDictionary(g => g.Key, g => g.Count());

            var labels = new List<string>();
            var values = new List<int>();
            for (var d = startDate; d <= AppTime.Today; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MM-dd"));
                values.Add(grouped.ContainsKey(d.Date) ? grouped[d.Date] : 0);
            }

            return Result.Success(new { labels, values });
        }

        /// <summary>
        /// 获取会员状态分布（饼图用）
        /// </summary>
        public virtual Result GetMemberDistribution()
        {
            var total = _fsql.Select<Member>().Count();
            var active = _fsql.Select<Member>().Where(m => m.Status == UserStatus.Active).Count();
            var banned = _fsql.Select<Member>().Where(m => m.Status == UserStatus.Banned).Count();

            var unverified = _fsql.Select<Member>().Where(m => m.RealNameStatus == RealNameStatus.Unverified).Count();
            var reviewing = _fsql.Select<Member>().Where(m => m.RealNameStatus == RealNameStatus.Reviewing).Count();
            var verified = _fsql.Select<Member>().Where(m => m.RealNameStatus == RealNameStatus.Verified).Count();
            var rejected = _fsql.Select<Member>().Where(m => m.RealNameStatus == RealNameStatus.Rejected).Count();

            return Result.Success(new
            {
                status = new[] {
                    new { name = "正常", value = active },
                    new { name = "封禁", value = banned },
                },
                realName = new[] {
                    new { name = "未认证", value = unverified },
                    new { name = "审核中", value = reviewing },
                    new { name = "已认证", value = verified },
                    new { name = "已拒绝", value = rejected },
                },
            });
        }

        /// <summary>
        /// 最近登录日志
        /// </summary>
        public virtual Result GetRecentLogins(int limit = 10)
        {
            var logs = _fsql.Select<LoginLog>()
                .OrderByDescending(l => l.CreateTime)
                .Take(limit)
                .ToList(l => new
                {
                    l.Username,
                    l.UserType,
                    l.IpAddress,
                    l.Status,
                    l.CreateTime,
                });

            return Result.Success(logs);
        }

        /// <summary>
        /// 最近注册的会员
        /// </summary>
        public virtual Result GetRecentMembers(int limit = 10)
        {
            var members = _fsql.Select<Member>()
                .OrderByDescending(m => m.CreatedTime)
                .Take(limit)
                .ToList(m => new
                {
                    m.Id,
                    m.Username,
                    m.Name,
                    m.Avatar,
                    m.Status,
                    m.RealNameStatus,
                    m.CreatedTime,
                });

            return Result.Success(members);
        }

        /// <summary>
        /// 待审核的实名认证
        /// </summary>
        public virtual Result GetPendingRealNames(int limit = 10)
        {
            var list = _fsql.Select<MemberRealName>()
                .Where(r => r.Status == RealNameStatus.Reviewing)
                .OrderByDescending(r => r.SubmitTime)
                .Take(limit)
                .ToList(r => new
                {
                    r.Id,
                    r.MemberId,
                    r.RealName,
                    r.SubmitTime,
                });

            // 查关联用户名
            var memberIds = list.Select(r => r.MemberId).Distinct().ToList();
            var memberMap = memberIds.Count > 0
                ? _fsql.Select<Member>().Where(m => memberIds.Contains(m.Id))
                    .ToList(m => new { m.Id, m.Username })
                    .ToDictionary(m => m.Id, m => m.Username)
                : new Dictionary<int, string>();

            var data = list.Select(r => new
            {
                r.Id,
                r.MemberId,
                MemberUsername = memberMap.ContainsKey(r.MemberId) ? memberMap[r.MemberId] : "-",
                r.RealName,
                r.SubmitTime,
            });

            return Result.Success(data);
        }

        /// <summary>
        /// 最近7天每天登录人数趋势
        /// </summary>
        public virtual Result GetLoginTrend(int days = 7)
        {
            if (days < 1) days = 7;
            if (days > 90) days = 90;

            var startDate = AppTime.Today.AddDays(-days + 1);

            var rawData = _fsql.Select<LoginLog>()
                .Where(l => l.CreateTime >= startDate && l.UserType == 0 && l.Status == 0)
                .ToList(l => new { l.UserId, l.CreateTime });

            var grouped = rawData
                .GroupBy(l => l.CreateTime.Date)
                .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().Count());

            var labels = new List<string>();
            var values = new List<int>();
            for (var d = startDate; d <= AppTime.Today; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MM-dd"));
                values.Add(grouped.ContainsKey(d.Date) ? grouped[d.Date] : 0);
            }

            return Result.Success(new { labels, values });
        }
    }
}
