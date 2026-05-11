-- =============================================
-- 数据库管理菜单 SQL（已安装系统手动添加用）
-- =============================================

-- 1. 插入菜单（挂载到系统管理下）
SET @parentId = (SELECT Id FROM `SB_Menu` WHERE Name = 'System' LIMIT 1);

INSERT INTO `SB_Menu` (
  `ParentId`, `Name`, `Path`, `Component`, `Title`, `Icon`, `OrderNo`, `HideInMenu`, `IgnoreKeepAlive`, `IsDeleted`
) VALUES (
  @parentId,
  'Database',
  '/system/database',
  '/database/List',
  '数据库管理',
  'ion:server-outline',
  10,
  b'0',
  b'0',
  b'0'
);

-- 2. 关联到超级管理员角色
SET @menuId = LAST_INSERT_ID();
SET @roleId = (SELECT Id FROM `SB_AdminRole` WHERE RoleKey = 'super' LIMIT 1);

INSERT INTO `SB_AdminRoleMenu` (`RoleId`, `MenuId`)
VALUES (@roleId, @menuId);

-- 3. 验证
-- SELECT * FROM `SB_Menu` WHERE Name = 'Database';
