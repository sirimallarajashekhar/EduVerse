// Optional local/CI k6 smoke load, not a production throughput guarantee.
// Usage: k6 run -e BASE_URL=http://localhost:5201 tools/load/k6-api-smoke.js
import http from 'k6/http';
import { check, sleep } from 'k6';
export const options = {
  scenarios: { api_reads: { executor: 'constant-vus', vus: 10, duration: '30s' } },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    http_req_duration: ['p(95)<500']
  }
};
const BASE_URL = __ENV.BASE_URL || 'http://localhost:5201';
export default function () {
  const response = http.get(BASE_URL + '/api/v1/platform/modules/paged?page=1&pageSize=10');
  check(response, { 'HTTP 200': r => r.status === 200, 'trace header': r => !!r.headers['X-Trace-Id'] });
  sleep(0.2);
}
