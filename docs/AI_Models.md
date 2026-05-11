# Entity Models & Enums

All tables prefixed `SB_`. All entities with `ISoftDelete` have global filter on `IsDeleted`.

---

## Admin (SB_Admin)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| ParentId | int | |
| Username | string(50) | |
| Password | string(200) | BCrypt hash or plaintext |
| RoleId | int, FK | |
| Name | string | |
| Nickname | string(50) | optional |
| Avatar | string | URL |
| Status | AdminStatus enum | |
| AuthenticatorKey | string | Google 2FA secret, optional |
| AuthenticatorBindTime | DateTime? | |
| CreatedTime | DateTime | auto `now()` |
| LastOnlineTime | DateTime? | |
| IsDeleted | bool | soft delete |

## AdminRole (SB_AdminRole)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Name | string(50) | |
| RoleKey | string(50) | unique identifier |
| IsDeleted | bool | |

## AdminRoleMenu (SB_AdminRoleMenu)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| RoleId | int, FK | |
| MenuId | int, FK | |

## Menu (SB_Menu)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| ParentId | int | |
| Name | string(100) | route name |
| Path | string(200) | route path |
| Component | string | component path |
| Redirect | string | optional |
| Title | string(100) | display title |
| Icon | string | icon name |
| OrderNo | int | sort order |
| HideInMenu | bool | |
| IgnoreKeepAlive | bool | |
| IsDeleted | bool | |

## Member (SB_Member)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Username | string | |
| Password | string | hashed |
| Name | string | display name |
| Avatar | string | URL |
| Status | UserStatus enum | |
| RealNameStatus | RealNameStatus enum | |
| CreditScore | int | default 100 |
| InvitationCode | string | optional |
| ParentId | int? | referrer |
| AncestryPath | string | format: `/1/5/`, optional |
| PaymentPassword | string | hashed, optional |
| CreatedTime | DateTime | auto |
| LastOnlineTime | DateTime | auto |
| IsDeleted | bool | |

## MemberRealName (SB_MemberRealName)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| MemberId | int, FK | |
| RealName | string | |
| IdNumber | string | |
| IdFrontImage | string | URL |
| IdBackImage | string | URL |
| Status | RealNameStatus enum | |
| RejectReason | string | optional |
| SubmitTime | DateTime | auto |
| ReviewTime | DateTime? | |
| ReviewedBy | int? | Admin.Id |

## LoginLog (SB_LoginLog)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| UserId | int | |
| UserType | int | 0=Member, 1=Admin |
| Username | string(100) | |
| IpAddress | string(50) | optional |
| UserAgent | string(500) | optional |
| Status | int | 0=Success, 1=Failed |
| CreateTime | DateTime | auto |

## SystemSetting (SB_SystemSetting)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| CategoryId | int, FK | |
| Key | string(100) | |
| Name | string(100) | display name |
| DisplayType | SystemSettingDisplayType enum | |
| DataContent | string | value, optional |
| DataContentConfig | string | JSON options config |
| CantDelete | bool | |
| Sort | int | |

## SystemSettingCategory (SB_SystemSettingCategory)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Key | string | |
| Name | string | |
| Icon | string | optional |
| Sort | int | |

## EmailConfig (SB_EmailConfig)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| SmtpServer | string | |
| SmtpPort | int | |
| Username | string | |
| Password | string | AES encrypted |
| FromEmail | string | |
| FromName | string | |
| EnableSsl | bool | |
| IsActive | bool | |
| CreatedAt | DateTime | auto |
| UpdatedAt | DateTime | auto |

## EmailTemplate (SB_EmailTemplate)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Code | string | unique |
| Name | string | |
| Subject | string | |
| Body | string | HTML |
| IsActive | bool | |
| CreatedAt | DateTime | auto |
| UpdatedAt | DateTime | auto |

## EmailLog (SB_EmailLog)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| TemplateId | int? | |
| Recipient | string | |
| Subject | string | |
| Status | EmailStatus enum | |
| ErrorMessage | string | optional |
| CreatedTime | DateTime | auto |

## EmailVerificationCode (SB_EmailVerificationCode)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Email | string | |
| Code | string | |
| Type | VerificationCodeType enum | |
| IsUsed | bool | |
| CreatedTime | DateTime | auto |
| ExpiresAt | DateTime | |

## ChunkUpload (SB_ChunkUpload)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| UploadId | string(32) | GUID |
| FileName | string(500) | |
| FileSize | long | |
| FileMd5 | string(32) | |
| ChunkSize | int | |
| TotalChunks | int | |
| Status | ChunkUploadStatus enum | |
| FilePath | string(500) | after merge |
| CreatedAt | DateTime | auto |
| CompletedAt | DateTime? | |

## ChunkUploadPart (SB_ChunkUploadPart)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| UploadId | string, FK | |
| ChunkIndex | int | |
| Size | long | |
| CreatedAt | DateTime | auto |

## DatabaseBackup (SB_DatabaseBackup)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| DatabaseName | string(100) | |
| FileName | string(200) | |
| FilePath | string(500) | |
| FileSize | long | |
| Duration | int | milliseconds |
| CreatedBy | string(100) | username or "MasterKey" |
| CreatedTime | DateTime | auto |
| Remark | string(500) | optional |

## BackgroundTaskLog (SB_BackgroundTaskLog)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| TaskName | string | |
| Source | LogSource enum | |
| LogLevel | TaskLogLevel enum | |
| Message | string | |
| CreatedTime | DateTime | auto |

## LicenseClient (SB_License_Client)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| Name | string(200) | |
| Domain | string(500) | |
| ServerIP | string(100) | optional |
| ClientKey | string(100) | UUID, optional |
| HardwareId | string(200) | hash, optional |
| Remark | string(500) | optional |
| Status | int | 0=Normal, 1=Disabled |
| CreatedTime | DateTime | auto |
| UpdatedTime | DateTime | auto |

## LicenseRecord (SB_License_Record)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| ClientId | int, FK | |
| Domain | string(500) | |
| ClientKey | string(100) | optional |
| HardwareId | string(200) | optional |
| VerifyMode | LicenseVerifyMode enum | |
| IssuedAt | DateTime | |
| ExpiresAt | DateTime | |
| AllowedRoutes | string(2000) | JSON array, optional |
| LicenseData | string(text) | RSA signed |
| IsDownloaded | bool | |
| Status | int | 0=Valid, 1=Revoked |
| CreatedTime | DateTime | auto |

## LicenseRequest (SB_License_Request)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| ClientId | int, FK | |
| Domain | string(500) | |
| ServerIP | string(100) | optional |
| ClientKey | string(100) | optional |
| HardwareId | string(200) | optional |
| Status | LicenseRequestStatus enum | |
| RejectReason | string(500) | optional |
| RecordId | int? | FK to LicenseRecord |
| CreatedTime | DateTime | auto |
| ProcessedTime | DateTime? | |

## LicenseVerifyLog (SB_License_VerifyLog)

| Field | Type | Note |
|-------|------|------|
| Id | int, PK, Identity | |
| ClientId | int, FK | |
| Domain | string(500) | |
| ServerIP | string(100) | optional |
| ClientKey | string(100) | optional |
| HardwareId | string(200) | optional |
| LicenseValid | bool | |
| Message | string(500) | optional |
| CreatedTime | DateTime | auto |

---

## Enums

### AdminStatus
| Value | Name |
|-------|------|
| 0 | Active |
| 1 | Disabled |

### UserStatus
| Value | Name |
|-------|------|
| 0 | Active |
| 1 | Banned |

### RealNameStatus
| Value | Name |
|-------|------|
| 0 | Unverified |
| 1 | Reviewing |
| 2 | Verified |
| 3 | Rejected |

### EmailStatus
| Value | Name |
|-------|------|
| 0 | Pending |
| 1 | Sent |
| 2 | Failed |

### VerificationCodeType
| Value | Name |
|-------|------|
| 0 | Register |
| 1 | ResetPassword |

### SystemSettingDisplayType
| Value | Name | Description |
|-------|------|-------------|
| 0 | Input | Text input |
| 1 | InputTextArea | Multiline text |
| 2 | RadioGroup | Radio buttons |
| 3 | Select | Dropdown |
| 4 | CheckboxGroup | Checkboxes |
| 5 | Switch | Toggle switch |
| 6 | ImageUpload | Single image |
| 7 | InputNumber | Number input |
| 8 | InputPassword | Password input |
| 9 | DatePicker | Date picker |
| 10 | TimePicker | Time picker |
| 11 | RangePicker | Date range |
| 12 | Rate | Star rating |
| 13 | AutoComplete | Autocomplete |
| 14 | Mentions | Mention input |
| 15 | TreeSelect | Tree select |
| 16 | IconPicker | Icon picker |
| 17 | MultiImageUpload | Multiple images |
| 18 | FileUpload | File upload |

### ChunkUploadStatus
| Value | Name |
|-------|------|
| 0 | Uploading |
| 1 | Completed |
| 2 | Expired |

### LicenseVerifyMode
| Value | Name |
|-------|------|
| 0 | ClientKeyOnly |
| 1 | HardwareIdOnly |
| 2 | Both |

### LicenseRequestStatus
| Value | Name |
|-------|------|
| 0 | Pending |
| 1 | Approved |
| 2 | Rejected |
