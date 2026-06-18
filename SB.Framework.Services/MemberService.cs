using Microsoft.Extensions.Logging;
using SB.Framework.Core.Attribute;
using SB.Framework.Core.Util;
using SB.Framework.Models;
using SB.Framework.Core;

namespace SB.Framework.Services
{
    [AutoDependencyInjection]
    public class MemberService : BaseCrudService<Member>, IModuleInitializer
    {
        #region IModuleInitializer

        public string ModuleName => "会员管理";
        public string ModuleKey => "Member";
        public int InitOrder => 20;

        /// <summary>本程序集可见的 Member Service 类型。</summary>
        public Type[] GetOwnedTypes() => new[] { typeof(MemberService) };

        /// <summary>MemberController 全名（位于 SB.Framework.WebApi，Services 无法 typeof 引用）。</summary>
        public string[] GetOwnedControllerNames() => new[]
        {
            "SB.Framework.WebApi.Controllers.MemberController",
        };

        public Type[] GetEntityTypes() => new[]
        {
            typeof(Member), typeof(MemberRealName),
        };

        public List<MenuDefinition> GetMenus() => new()
        {
            new MenuDefinition
            {
                Name = "Member", Path = "/member", Component = "LAYOUT",
                Title = "会员管理", Icon = "ion:people-outline", OrderNo = 10,
                Children = new()
                {
                    new() { Name = "MemberList", Path = "/member/list", Component = "/member/List", Title = "会员列表", Icon = "ion:person-outline", OrderNo = 1 },
                    new() { Name = "MemberForm", Path = "/member/form", Component = "/member/Form", Title = "编辑会员", OrderNo = 3, HideInMenu = true },
                    new() { Name = "MemberTree", Path = "/member/tree", Component = "/member/Tree", Title = "推荐树", Icon = "ion:git-branch-outline", OrderNo = 2 },
                    new() { Name = "RealName", Path = "/member/real-name", Component = "/real-name/List", Title = "实名审核", Icon = "ion:checkmark-circle-outline", OrderNo = 4 },
                }
            },
        };

        #endregion

        IFreeSql _fsql;

        public MemberService(IFreeSql fsql, ILogger<MemberService> logger) : base(fsql, logger)
        {
            _fsql = fsql;
        }

        public virtual Member GetMemberById(int memberId)
        {
            return Select.Where(c => c.Id == memberId).First();
        }

        public virtual Result GetList(int? id = null, string? keyword = null, UserStatus? status = null,
            string field = "", string order = "", int page = 1, int pageSize = 20)
        {
            var query = Select;
            query = query.WhereIf(id.HasValue, c => c.Id == id);
            query = query.WhereIf(!string.IsNullOrEmpty(keyword),
                c => c.Username.Contains(keyword!) || c.Name.Contains(keyword!));
            query = query.WhereIf(status.HasValue, c => c.Status == status!.Value);
            query = ApplySorting(query, field, order);

            var members = query.Count(out long dataCount).Page(page, pageSize).ToList();
            if (members.Count == 0)
                return Result.Page(new List<object>(), dataCount);

            // 查询邀请人名称
            var parentIds = members.Where(m => m.ParentId.HasValue).Select(m => m.ParentId!.Value).Distinct().ToList();
            var parentMap = parentIds.Count > 0
                ? _fsql.Select<Member>().Where(m => parentIds.Contains(m.Id))
                    .ToList(m => new { m.Id, m.Username })
                    .ToDictionary(m => m.Id, m => m.Username)
                : new Dictionary<int, string>();

            // 查询直接下级数量
            var memberIds = members.Select(m => m.Id).ToList();
            var directChildren = _fsql.Select<Member>()
                .Where(m => m.ParentId.HasValue && memberIds.Contains(m.ParentId.Value))
                .ToList(m => m.ParentId!.Value);
            var directCountMap = directChildren.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());

            var data = members.Select(m => new
            {
                m.Id, m.Username, m.Name,
                Avatar = m.Avatar,
                m.Status, m.CreditScore, m.RealNameStatus, m.InvitationCode,
                m.CreatedTime, m.LastOnlineTime,
                m.ParentId,
                ParentName = m.ParentId.HasValue && parentMap.ContainsKey(m.ParentId.Value) ? parentMap[m.ParentId.Value] : null,
                DirectInviteCount = directCountMap.ContainsKey(m.Id) ? directCountMap[m.Id] : 0,
            }).ToList();

            return Result.Page(data, dataCount);
        }

        /// <summary>
        /// 获取会员树形结构数据（指定会员及其所有后代）
        /// </summary>
        public virtual Result GetMemberTree(int memberId)
        {
            var root = Select.Where(c => c.Id == memberId).First();
            if (root == null)
                return Result.Fail("用户不存在");

            var descendants = Select
                .Where(c => c.AncestryPath!.Contains($"/{memberId}/"))
                .ToList(c => new
                {
                    c.Id, c.ParentId, c.Username, c.Name, c.InvitationCode, c.CreatedTime, c.Status
                });

            var childCountMap = descendants.Where(d => d.ParentId.HasValue)
                .GroupBy(d => d.ParentId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            var rootDirectCount = descendants.Count(d => d.ParentId == memberId);

            return Result.Success(new
            {
                root = new
                {
                    root.Id, root.Username, root.Name, root.InvitationCode,
                    root.CreatedTime, root.Status, DirectChildCount = rootDirectCount
                },
                descendants = descendants.Select(d => new
                {
                    d.Id, d.ParentId, d.Username, d.Name, d.InvitationCode,
                    d.CreatedTime, d.Status,
                    DirectChildCount = childCountMap.ContainsKey(d.Id) ? childCountMap[d.Id] : 0
                }),
            });
        }

        /// <summary>
        /// 获取直接子级会员列表（懒加载树用）
        /// </summary>
        public virtual Result GetChildren(int? parentId)
        {
            var query = parentId.HasValue
                ? Select.Where(c => c.ParentId == parentId.Value)
                : Select.Where(c => c.ParentId == null);

            var members = query.OrderByDescending(c => c.CreatedTime).ToList();
            var memberIds = members.Select(m => m.Id).ToList();

            var childCounts = memberIds.Count > 0
                ? _fsql.Select<Member>()
                    .Where(m => m.ParentId.HasValue && memberIds.Contains(m.ParentId.Value))
                    .ToList(m => m.ParentId!.Value)
                    .GroupBy(id => id)
                    .ToDictionary(g => g.Key, g => g.Count())
                : new Dictionary<int, int>();

            var data = members.Select(m => new
            {
                m.Id, m.Username, m.Name, m.InvitationCode, m.Status,
                m.CreatedTime, m.ParentId,
                DirectChildCount = childCounts.ContainsKey(m.Id) ? childCounts[m.Id] : 0,
                IsLeaf = !childCounts.ContainsKey(m.Id),
            });

            return Result.Success(data);
        }

        public virtual Result Create(Member member)
        {
            var existing = Select.Where(c => c.Username == member.Username).First();
            if (existing != null)
                return Result.Fail($"用户名 {member.Username} 已存在");

            if (!string.IsNullOrEmpty(member.Password))
                member.Password = PasswordUtil.EncryptPassword(member.Password);

            member.CreatedTime = AppTime.Now;
            member = Insert(member);
            return Result.Success("创建成功", member);
        }

        public virtual Result Edit(Member member)
        {
            var existing = Select.Where(c => c.Id == member.Id).First();
            if (existing == null)
                return Result.Fail("用户不存在");

            existing.Name = member.Name;
            existing.Avatar = member.Avatar;
            existing.Status = member.Status;
            existing.CreditScore = member.CreditScore;
            existing.RealNameStatus = member.RealNameStatus;

            Update(existing);
            return Result.Success("更新成功");
        }

        /// <summary>
        /// 重置登录密码
        /// </summary>
        public virtual Result ResetPassword(int id, string newPassword)
        {
            var member = Select.Where(c => c.Id == id).First();
            if (member == null)
                return Result.Fail("用户不存在");

            member.Password = PasswordUtil.EncryptPassword(newPassword);
            UpdateDiy.UpdateColumns(c => c.Password).SetSource(member).ExecuteAffrows();
            return Result.Success("密码已重置");
        }

        /// <summary>
        /// 获取会员推荐树
        /// </summary>
        public virtual Result GetReferralTree(int memberId)
        {
            var member = Select.Where(c => c.Id == memberId).First();
            if (member == null)
                return Result.Fail("用户不存在");

            var ancestors = new List<object>();
            if (!string.IsNullOrEmpty(member.AncestryPath))
            {
                var ancestorIds = member.AncestryPath.Trim('/').Split('/')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();

                if (ancestorIds.Count > 0)
                {
                    var ancestorMembers = _fsql.Select<Member>()
                        .Where(m => ancestorIds.Contains(m.Id))
                        .ToList();

                    ancestors = ancestorIds
                        .Select(id => ancestorMembers.FirstOrDefault(m => m.Id == id))
                        .Where(m => m != null)
                        .Select(m => (object)new { m!.Id, m.Username, m.Name })
                        .ToList();
                }
            }

            var children = Select.Where(c => c.ParentId == memberId)
                .ToList(c => new { c.Id, c.Username, c.Name, c.CreatedTime });

            var totalDescendants = Select.Where(c => c.AncestryPath!.Contains($"/{memberId}/")).Count();

            return Result.Success(new
            {
                member = new { member.Id, member.Username, member.Name, member.ParentId, member.AncestryPath },
                ancestors,
                children,
                totalDescendants,
            });
        }

        /// <summary>
        /// 会员搜索（下拉选择用）
        /// </summary>
        public virtual Result Search(string? keyword = null, int limit = 20)
        {
            var query = Select;

            if (!string.IsNullOrEmpty(keyword))
            {
                if (int.TryParse(keyword, out int id))
                    query = query.Where(c => c.Id == id || c.Username.Contains(keyword) || c.Name.Contains(keyword));
                else
                    query = query.Where(c => c.Username.Contains(keyword) || c.Name.Contains(keyword));
            }
            else
            {
                limit = 10;
            }

            var list = query.OrderByDescending(c => c.LastOnlineTime).Take(limit).ToList(c => new
            {
                c.Id,
                c.Username,
                c.Name,
                c.Avatar,
            });

            return Result.Success(list);
        }
    }
}
