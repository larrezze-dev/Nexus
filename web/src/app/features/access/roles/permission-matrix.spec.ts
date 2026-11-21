import { Permission } from '../../../api/platform/models/permission';
import { buildPermissionMatrix, diffPermissions, humanizeCode } from './permission-matrix';

const perm = (resourceType: string, action: Permission['action'], caption = ''): Permission => ({
  code: `${resourceType}:${action}`,
  resourceType,
  action,
  captionI18n: { de: caption },
});

describe('permission matrix', () => {
  const labels = {
    language: 'en',
    resource: (r: string) => ({ AUDIT: 'Audit', ORG_STRUCTURE: 'Legal entities' })[r] ?? humanizeCode(r),
    action: (a: string) => ({ READ: 'View', CREATE: 'Create', UPDATE: 'Edit' })[a] ?? humanizeCode(a),
  };

  it('orders columns canonically and rows by readable label, leaving gaps empty', () => {
    const matrix = buildPermissionMatrix(
      [perm('ORG_STRUCTURE', 'UPDATE'), perm('ORG_STRUCTURE', 'READ'), perm('AUDIT', 'READ', 'View audit'), perm('ROLE_ASSIGNMENT', 'CREATE')],
      labels,
    );
    expect(matrix.columns.map((c) => c.label)).toEqual(['View', 'Create', 'Edit']);
    expect(matrix.rows.map((r) => r.label)).toEqual(['Audit', 'Legal entities', 'Role assignment']);
    expect(matrix.rows[0].cells.get('READ')).toEqual({ code: 'AUDIT:READ', label: 'Audit — View', caption: 'View audit' });
    expect(matrix.rows[0].cells.has('CREATE')).toBe(false);
  });

  it('diffPermissions lists what to open and what to close', () => {
    expect(diffPermissions(new Set(['A', 'B']), new Set(['B', 'C']))).toEqual({ added: ['C'], removed: ['A'] });
    expect(diffPermissions(new Set(['A']), new Set(['A']))).toEqual({ added: [], removed: [] });
  });
});
