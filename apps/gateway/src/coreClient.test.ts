import assert from 'node:assert/strict';
import test from 'node:test';
import { coreEndpoint } from './coreClient.js';
import { executeCodeTool, listCodeToolIds } from './codeTools.js';

test('coreEndpoint resolves API paths against the configured core URL', () => {
  assert.equal(coreEndpoint('/api/v1/health'), 'http://127.0.0.1:48731/api/v1/health');
});

test('Code tools expose programming-domain execution contracts', async () => {
  assert.deepEqual(listCodeToolIds().sort(), ['apply_patch', 'review_format', 'sandbox_exec', 'search_files']);

  const search = await executeCodeTool('search_files', { arguments: { query: 'AgentWorkflowRuntime' } });
  assert.equal(search?.requires_approval, false);
  assert.match(search?.status ?? '', /^(native|stubbed)$/);
  if (search?.status === 'native') {
    assert.equal(search.data.query, 'AgentWorkflowRuntime');
    assert.ok(Array.isArray(search.data.matches));
  } else {
    assert.deepEqual(search?.data.argument_keys, ['query']);
  }

  const patch = await executeCodeTool('apply_patch', { cwd: 'D:/github/TinadecCode' });
  assert.equal(patch?.requires_approval, true);
  assert.equal(patch?.status, 'blocked');

  assert.equal(await executeCodeTool('unknown_tool'), null);
});
