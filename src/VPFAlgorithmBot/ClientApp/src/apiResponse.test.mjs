import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readApiResponse } from './apiResponse.ts'

test('successful empty saves and deletes do not parse JSON', async () => {
  assert.equal(await readApiResponse(new Response(null, { status: 200 })), undefined)
  assert.equal(await readApiResponse(new Response(null, { status: 204 })), undefined)
})
test('JSON catalogs still deserialize', async () => {
  assert.deepEqual(await readApiResponse(Response.json({ chats: [{ id: 1 }] })), { chats: [{ id: 1 }] })
})
test('validation and empty authorization failures are readable', async () => {
  await assert.rejects(readApiResponse(Response.json('Оберіть алгоритм.', { status: 400 })), /Оберіть алгоритм/)
  await assert.rejects(readApiResponse(new Response(null, { status: 403 })), /сеанс адміністратора/)
  await assert.rejects(readApiResponse(Response.json({ detail: 'Такий запис існує.' }, { status: 409 })), /Такий запис/)
})
test('HTML error pages are not shown as raw markup', async () => {
  await assert.rejects(readApiResponse(new Response('<html>internal exception</html>', { status: 500 })), /HTTP 500/)
  await assert.rejects(readApiResponse(new Response('<html>offline</html>')), /неочікувану відповідь/)
})
