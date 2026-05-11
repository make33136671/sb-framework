-- =============================================
-- 部署管理菜单 SQL（已安装系统手动添加用）
-- =============================================

-- 1. 查询系统管理菜单 ID 和当前最大菜单 ID
-- SELECT Id FROM `SB_Menu` WHERE Name = 'System';
-- SELECT MAX(Id) FROM `SB_Menu`;

-- 2. 插入菜单（请将 ParentId 替换为实际的系统管理菜单 ID，Id 替换为 MAX(Id)+1）
INSERT INTO `SB_Menu` (
  `ParentId`, `Name`, `Path`, `Component`, `Title`, `Icon`, `OrderNo`, `HideInMenu`, `IsDeleted`
) VALUES (
  (SELECT Id FROM `SB_Menu` WHERE Name = 'System' LIMIT 1),
  'Deploy',
  '/system/deploy',
  '/deploy/List',
  '部署管理',
  'ion:cloud-upload-outline',
  9,
  b'0',
  b'0'
);

-- 3. 关联到超级管理员角色
INSERT INTO `SB_AdminRoleMenu` (`RoleId`, `MenuId`)
SELECT
  (SELECT Id FROM `SB_AdminRole` WHERE RoleKey = 'super' LIMIT 1),
  Id
FROM `SB_Menu`
WHERE Name = 'Deploy';

-- 4. 验证
-- SELECT * FROM `SB_Menu` WHERE Name = 'Deploy';
