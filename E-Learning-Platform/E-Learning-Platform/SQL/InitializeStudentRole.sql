-- First, ensure the STUDENT role exists
IF NOT EXISTS (SELECT 1 FROM ROLES WHERE ROLE_NAME = 'STUDENT')
BEGIN
    INSERT INTO ROLES (ROLE_NAME, DESCRIPTION, CREATED_DATE)
    VALUES ('STUDENT', 'Default role for all new users. Has basic access to learning features.', GETDATE());
END

-- Get the STUDENT role ID
DECLARE @StudentRoleId INT;
SELECT @StudentRoleId = ROLE_ID FROM ROLES WHERE ROLE_NAME = 'STUDENT';

-- Get the Admin User ID for audit purposes
DECLARE @AdminUserId INT;
SELECT TOP 1 @AdminUserId = u.USER_ID 
FROM USERS u
JOIN USER_ROLES ur ON u.USER_ID = ur.USER_ID
JOIN ROLES r ON ur.ROLE_ID = r.ROLE_ID
WHERE r.ROLE_NAME = 'ADMIN';

-- Ensure basic student permissions exist
INSERT INTO PERMISSIONS (PERMISSION_NAME, DESCRIPTION, CREATED_DATE)
SELECT permission_name, description, GETDATE()
FROM (
    VALUES 
        ('COURSE.VIEW', 'View available courses'),
        ('CONTENT.VIEW', 'View course content'),
        ('PROFILE.EDIT', 'Edit own profile'),
        ('ENROLLMENT.MANAGE', 'Manage course enrollments')
) AS p(permission_name, description)
WHERE NOT EXISTS (
    SELECT 1 FROM PERMISSIONS 
    WHERE PERMISSION_NAME = p.permission_name
);

-- Assign basic permissions to STUDENT role
INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, ASSIGNED_DATE, ASSIGNED_BY)
SELECT 
    @StudentRoleId,
    p.PERMISSION_ID,
    GETDATE(),
    @AdminUserId
FROM PERMISSIONS p
WHERE p.PERMISSION_NAME IN ('COURSE.VIEW', 'CONTENT.VIEW', 'PROFILE.EDIT', 'ENROLLMENT.MANAGE')
AND NOT EXISTS (
    SELECT 1 FROM ROLE_PERMISSIONS rp 
    WHERE rp.ROLE_ID = @StudentRoleId 
    AND rp.PERMISSION_ID = p.PERMISSION_ID
);

-- Assign STUDENT role to users who don't have any roles
INSERT INTO USER_ROLES (USER_ID, ROLE_ID)
SELECT u.USER_ID, @StudentRoleId
FROM USERS u
WHERE NOT EXISTS (
    SELECT 1 FROM USER_ROLES ur 
    WHERE ur.USER_ID = u.USER_ID
);

-- Log the role assignments in the audit table
INSERT INTO ROLE_CHANGE_AUDIT (USER_ID, OLD_ROLE_ID, NEW_ROLE_ID, CHANGED_BY, CHANGE_REASON, CHANGE_DATE)
SELECT 
    u.USER_ID,
    NULL,
    @StudentRoleId,
    @AdminUserId,
    'Automatic assignment of default STUDENT role',
    GETDATE()
FROM USERS u
WHERE NOT EXISTS (
    SELECT 1 FROM USER_ROLES ur 
    WHERE ur.USER_ID = u.USER_ID
); 