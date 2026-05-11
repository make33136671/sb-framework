-- =============================================
-- 错误日志管理菜单 SQL
-- =============================================

-- 说明：
-- 1. 请先查询当前最大的菜单 ID，然后修改下面的 ID 值
-- 2. ParentId 根据实际情况调整（0 为顶级菜单，或者挂载到已有的父菜单下）
-- 3. 如果要挂载到"系统管理"菜单下，需要查询"系统管理"的 ID 并设置为 ParentId

-- 顶级菜单：错误日志管理
-- 假设当前最大 ID 为 53，新菜单从 54 开始
-- 如果要作为子菜单，将 ParentId 改为父菜单的 ID
INSERT INTO `SB_Menu` (
  `Id`,
  `ParentId`,
  `Name`,
  `Path`,
  `Component`,
  `Redirect`,
  `Title`,
  `Icon`,
  `OrderNo`,
  `HideInMenu`,
  `IgnoreKeepAlive`
) VALUES (
  54,                              -- 菜单ID（请根据实际情况调整）
  0,                               -- 父菜单ID（0为顶级菜单，或改为其他菜单ID）
  'ErrorLogList',                  -- 路由名称
  '/ErrorLogList',                 -- 路由路径
  '/error-log/List',               -- 组件路径（对应 views/error-log/List.vue）
  NULL,                            -- 重定向路径
  '错误日志',                       -- 菜单标题
  'ion:bug-outline',               -- 图标
  999,                             -- 排序号（越小越靠前）
  b'0',                            -- 是否隐藏（b'0'显示，b'1'隐藏）
  b'0'                             -- 是否忽略缓存
);

-- =============================================
-- 使用说明
-- =============================================

-- 1. 查询当前最大菜单 ID：
-- SELECT MAX(Id) FROM `SB_Menu`;

-- 2. 如果要挂载到"系统管理"等父菜单下，先查询父菜单ID：
-- SELECT Id, Title FROM `SB_Menu` WHERE Title = '系统管理';

-- 3. 修改上面 SQL 中的 Id 和 ParentId 值后执行

-- 4. 验证插入结果：
-- SELECT * FROM `SB_Menu` WHERE Name = 'ErrorLogList';

-- 5. 如果菜单不显示，检查：
--    - 用户角色是否有该菜单权限（SB_GuanliyuanJueseMenu 表）
--    - 组件路径是否正确（对应 views/ 下的文件路径，不含 .vue 后缀）
--    - HideInMenu 是否设置为 b'0'（显示）
