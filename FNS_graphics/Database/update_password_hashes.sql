BEGIN;

UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$0csJi/uN26wL8pyxrIxE3A==$ppnqZWjAaqFfEWc4QU91KruwqT6tJVTS+LNdfktPgBs=' WHERE login = 'admin_kirill';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$xmtMwQDBj6OcvGUnyJptkQ==$IBFgt3TOTnUXU9oNljFxZk9JHt0Tt8pbRyhguQ5IXO8=' WHERE login = 'admin_marta';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$29H8AqMvvjmH0o3/nLvbcQ==$Q63n11VWb5D+eoZGVM+/hKMMpNK3jCYl/9pmwf2HD48=' WHERE login = 'ivan_solver';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$AFDZOcQjFqX+mQvHPUK2MQ==$riaGVEq9dJCcyuoYTdSUDktxiTHN4g/MUD7njTcqydk=' WHERE login = 'lena_crypto';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$q+QCK4gICb28spdOiuRTWA==$VN7fO6b/iFeLsWYj9nmauxk5PDUNkAXIXG1ulHoLUzo=' WHERE login = 'test_reader';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$khl8qfrROdBr6hcYQ6czCA==$rRw/272UiV4BnSarWbwbcJ2cvU/FdVnoB7l+2RnpIu8=' WHERE login = 'blocked_nick';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$SEj2+Pfcug3ToNmKpPDbFw==$SyW7RCkW5QoYVtUpjoEz8uTjFLdsDh2dCbMdA6usyvk=' WHERE login = 'olga_texts';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$6VjvuvLY0otV+Sz9KMfBXQ==$ybtMlbB8uYwdH3csHe1cCZD5GGekQ6jDLzH5Zg5oDu0=' WHERE login = 'max_factorial';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$d0dvLOHEYIEy1ZA+3KNsrg==$lyUiPbyZinBnQrBcelsb2NAQqC74rDwsxuiXWE6Zlqw=' WHERE login = 'vera_archive';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$DjhOTZa+/zVs6yKMhBqeZA==$/qBI+N3aXFIcMGyHJknITkOLLo88/g+6dA7RlRPGMx4=' WHERE login = 'pavel_notes';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$OY3sdFaXmmR7D5M6rIM5hw==$ujhmRIbrI2zuKtlsvOSAiRjzRQt5Hw3kDfuPugXJ5mQ=' WHERE login = 'sasha_lab';
UPDATE "FNS_log".users SET password_hash = 'pbkdf2-sha256$100000$j+viqSRiCZKLTvCO9JKYxQ==$ucMS5+ZmQVNQ7w/ZThRkmpdIz7rWxIQEEiwzj06ZSIU=' WHERE login = 'guest_demo';

COMMIT;
