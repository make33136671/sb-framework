using FreeSql;
using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Core;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class RealNameService : BaseCrudService<MemberRealName>
    {
        IFreeSql _fsql;

        public RealNameService(IFreeSql fsql, ILogger<RealNameService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Result GetList(RealNameStatus? status = null, string? keyword = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select;
            query = query.WhereIf(status.HasValue, c => c.Status == status!.Value);

            if (!string.IsNullOrEmpty(keyword))
            {
                var memberIds = _fsql.Select<Member>()
                    .Where(m => m.Username.Contains(keyword) || m.Name.Contains(keyword))
                    .ToList(m => m.Id);
                if (memberIds.Count > 0)
                    query = query.Where(c => memberIds.Contains(c.MemberId));
                else
                    query = query.Where(c => c.RealName.Contains(keyword) || c.IdNumber.Contains(keyword));
            }

            query = ApplySorting(query, field, order);

            var records = query.Count(out long dataCount).Page(page, pageSize).ToList();
            if (records.Count == 0)
                return Result.Page(new List<object>(), dataCount, "数据查询成功");

            var memberIdList = records.Select(r => r.MemberId).Distinct().ToList();
            var memberMap = _fsql.Select<Member>()
                .Where(m => memberIdList.Contains(m.Id))
                .ToList(m => new { m.Id, m.Username, m.Name })
                .ToDictionary(m => m.Id);

            var data = records.Select(r => new
            {
                r.Id,
                r.MemberId,
                MemberUsername = memberMap.ContainsKey(r.MemberId) ? memberMap[r.MemberId].Username : null,
                MemberName = memberMap.ContainsKey(r.MemberId) ? memberMap[r.MemberId].Name : null,
                r.RealName,
                r.IdNumber,
                r.IdFrontImage,
                r.IdBackImage,
                Status = (int)r.Status,
                r.RejectReason,
                r.SubmitTime,
                r.ReviewTime,
            }).ToList();

            return Result.Page(data, dataCount, "数据查询成功");
        }

        public virtual Result Approve(int id, int reviewedBy)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null)
                return Result.Fail("记录不存在");
            if (record.Status != RealNameStatus.Reviewing)
                return Result.Fail("该记录不在审核中状态");

            record.Status = RealNameStatus.Verified;
            record.ReviewTime = AppTime.Now;
            record.ReviewedBy = reviewedBy;
            Update(record);

            _fsql.Update<Member>()
                .Where(m => m.Id == record.MemberId)
                .Set(m => m.RealNameStatus, RealNameStatus.Verified)
                .ExecuteAffrows();

            return Result.Success("审核通过");
        }

        public virtual Result Reject(int id, int reviewedBy, string? reason = null)
        {
            var record = Select.Where(c => c.Id == id).First();
            if (record == null)
                return Result.Fail("记录不存在");
            if (record.Status != RealNameStatus.Reviewing)
                return Result.Fail("该记录不在审核中状态");

            record.Status = RealNameStatus.Rejected;
            record.RejectReason = reason;
            record.ReviewTime = AppTime.Now;
            record.ReviewedBy = reviewedBy;
            Update(record);

            _fsql.Update<Member>()
                .Where(m => m.Id == record.MemberId)
                .Set(m => m.RealNameStatus, RealNameStatus.Rejected)
                .ExecuteAffrows();

            return Result.Success("已拒绝");
        }
    }
}
