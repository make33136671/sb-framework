using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SB.Framework.Models.Dto
{
    public class VbenMenu
    {
        public VbenMenu() { }

        public VbenMenu(Menu menu, List<Menu> allMenus = null)
        {
            Id = menu.Id;
            ParentId = menu.ParentId;
            Name = menu.Name;
            Path = menu.Path;
            Component = string.IsNullOrEmpty(menu.Component) ? "PARENT" : menu.Component;
            Redirect = menu.Redirect;
            Meta = new Meta
            {
                Title = menu.Title,
                Icon = menu.Icon,
                OrderNo = menu.OrderNo,
                HideInMenu = menu.HideInMenu,
                IgnoreKeepAlive = menu.IgnoreKeepAlive,
            };

            if (allMenus != null)
            {
                Children = allMenus
                    .Where(m => m.ParentId == menu.Id)
                    .OrderBy(m => m.OrderNo)
                    .Select(m => new VbenMenu(m, allMenus))
                    .ToList();
            }
            else
            {
                Children = new List<VbenMenu>();
            }
        }
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string Component { get; set; }
        public string Redirect { get; set; }
        public Meta Meta { get; set; }
        public List<VbenMenu> Children { get; set; }

        /// <summary>
        /// 便利属性 — 返回 Meta.Title
        /// </summary>
        public string Title => Meta?.Title;

        /// <summary>
        /// 便利属性 — 返回 Meta.OrderNo
        /// </summary>
        public int OrderNo => Meta?.OrderNo ?? 0;
    }

    public class Meta
    {
        public string Title { get; set; }
        public string Icon { get; set; }
        public int OrderNo { get; set; }
        public bool HideInMenu { get; set; }
        public bool IgnoreKeepAlive { get; set; }
    }
}
