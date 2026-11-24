-- Dev data must be fictitious (decided by Luciano, 2026-11-21). The names created by
-- V202610241002__seed__org_dev.sql ("Nüssli ...") are real company names, so this file
-- replaces them with the fictitious SAPE names. It only UPDATEs rows the old seed
-- created, by their fixed ids; the old seed is versioned and merged, so it is not edited.
--
-- Changed: org.party legal_name / display_name for the 5 organisation parties, and the
-- org."group" code (NUSSLI -> SAPE). Nothing else is touched (legal entities, business
-- units, warehouses, locations and users keep their codes).
--
-- Idempotent: plain UPDATEs by primary key, so re-running sets the same values again.
-- Requires db/seed/common (SYSTEM user) and db/seed/dev/V202610241002 (the rows) to be applied first.

UPDATE org.party
   SET legal_name   = 'SAPE Group',
       display_name = 'SAPE',
       updated_at   = now(),
       updated_by   = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0101';

UPDATE org.party
   SET legal_name   = 'SAPE smart bud AG',
       display_name = 'SAPE CH',
       updated_at   = now(),
       updated_by   = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0102';

UPDATE org.party
   SET legal_name   = 'SAPE smart bud GmbH',
       display_name = 'SAPE DE',
       updated_at   = now(),
       updated_by   = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0103';

UPDATE org.party
   SET legal_name   = 'SAPE Events CH',
       display_name = 'Events CH',
       updated_at   = now(),
       updated_by   = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0104';

UPDATE org.party
   SET legal_name   = 'SAPE Events DE',
       display_name = 'Events DE',
       updated_at   = now(),
       updated_by   = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0105';

UPDATE org."group"
   SET code       = 'SAPE',
       updated_at = now(),
       updated_by = '00000000-0000-7000-8000-000000000001'
 WHERE id = '00000000-0000-7000-8000-0000000b0201';
