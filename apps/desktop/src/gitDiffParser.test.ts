import { describe, expect, it } from 'vitest';
import { parseUnifiedDiff } from './gitDiffParser';

describe('parseUnifiedDiff', () => {
  it('parses modified file hunks with line numbers', () => {
    const parsed = parseUnifiedDiff(`diff --git a/src/a.ts b/src/a.ts
--- a/src/a.ts
+++ b/src/a.ts
@@ -1,3 +1,4 @@
 const a = 1
-const b = 2
+const b = 3
+const c = 4
 const d = 5
`);

    expect(parsed.files).toHaveLength(1);
    const file = parsed.files[0];
    expect(file.path).toBe('src/a.ts');
    expect(file.hunks[0].lines.map((line) => [line.change, line.old_line_number, line.new_line_number])).toEqual([
      ['context', 1, 1],
      ['delete', 2, null],
      ['add', null, 2],
      ['add', null, 3],
      ['context', 3, 4]
    ]);
  });

  it('parses new files and renames', () => {
    const parsed = parseUnifiedDiff(`diff --git a/old.txt b/new.txt
similarity index 100%
rename from old.txt
rename to new.txt
diff --git a/created.txt b/created.txt
new file mode 100644
--- /dev/null
+++ b/created.txt
@@ -0,0 +1,1 @@
+hello
`);

    expect(parsed.files[0]).toMatchObject({
      path: 'new.txt',
      previous_path: 'old.txt',
      change_type: 'renamed'
    });
    expect(parsed.files[1]).toMatchObject({
      path: 'created.txt',
      previous_path: null,
      change_type: 'added'
    });
  });

  it('parses deleted files', () => {
    const parsed = parseUnifiedDiff(`diff --git a/removed.txt b/removed.txt
deleted file mode 100644
--- a/removed.txt
+++ /dev/null
@@ -1,2 +0,0 @@
-old
-gone
`);

    expect(parsed.files[0]).toMatchObject({
      path: 'removed.txt',
      previous_path: 'removed.txt',
      change_type: 'deleted'
    });
    expect(parsed.files[0].hunks[0].lines.map((line) => [line.change, line.old_line_number, line.new_line_number])).toEqual([
      ['delete', 1, null],
      ['delete', 2, null]
    ]);
  });

  it('keeps binary file notices', () => {
    const parsed = parseUnifiedDiff(`diff --git a/image.png b/image.png
Binary files a/image.png and b/image.png differ
`);

    expect(parsed.files[0].binary).toBe(true);
    expect(parsed.files[0].notice).toContain('Binary files');
  });
});
