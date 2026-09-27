# RabWorker Fix Prioritization at 50% Capacity (Jev Scoring)

_Date: 2026-09-26 · Input: findings in [`CODE_REVIEW.md`](CODE_REVIEW.md) · Model: TypeSafe `jev-latest`_

## Method

For each of the 14 findings in `CODE_REVIEW.md`, Jev scored three things on a 0–3 scale (one request per finding):

| Question | 0 | 1 | 2 | 3 |
|---|---|---|---|---|
| **Impact**: worst realistic consequence in production | Negligible / dev-only | Minor, easily recovered | Degraded processing or moderate security exposure | Silent outage, data loss, or PII exposure/tampering |
| **Likelihood**: triggered in production within a year | Very unlikely | Possible (rare event or misconfiguration) | Likely (outages, deploys, config mistakes) | Near certain |
| **Effort**: cost of the fix | Trivial (<1h) | Small (a few hours) | Medium (~1 day) | Large (multiple days) |

Code then did the ranking:

- **Risk** = (impact + 1) × (likelihood + 1), which ranges from 1 to 16.
- **Cost** = the effort score converted to rough hours: level 0 = 1h, 1 = 3h, 2 = 8h, 3 = 20h, interpolated between levels.
- **Value** = risk ÷ cost.
- **Budget** = 50% of total cost (30.6h of 61.2h). Items were taken in order of value while they fit the budget.

The hour conversion is a rough assumption, not a measurement. The script is `prioritize.py`, kept outside the repo.

## Jev's scores (ranked by value)

**Conf.** is the lowest confidence Jev reported across the three questions for that finding.

| Rank | ID | Finding | Impact | Likelihood | Effort | Risk | Cost (h) | Value | Conf. | In 50%? |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | S2 | docker-compose ports exposed on all interfaces | 2.99 | 1.27 | 0.06 | 9.1 | 1.1 | 8.09 | 0.37 | ✅ |
| 2 | S4 | Whitelist check only in the parser (SQL injection) | 2.79 | 0.49 | 0.11 | 5.6 | 1.2 | 4.63 | 0.51 | ✅ |
| 3 | S1 | Hardcoded default credentials | 2.99 | 1.71 | 0.85 | 10.8 | 2.7 | 4.00 | 0.61 | ✅ |
| 4 | B4 | Minor issues (shutdown, leak, config validation) | 1.82 | 1.99 | 0.60 | 8.4 | 2.2 | 3.83 | 0.49 | ✅ |
| 5 | B3 | Test client fixed seed | 2.23 | 0.16 | 0.11 | 3.7 | 1.2 | 3.07 | 0.23 | ✅ |
| 6 | B2 | Setup errors dead-letter every message | 2.78 | 1.74 | 1.08 | 10.4 | 3.4 | 3.05 | 0.57 | ✅ |
| 7 | O2 | No health check | 2.94 | 2.28 | 1.31 | 12.9 | 4.6 | 2.84 | 0.54 | ✅ |
| 8 | S5 | No TLS for PII in transit | 3.00 | 2.38 | 1.44 | 13.5 | 5.2 | 2.60 | 0.00 | ✅ |
| 9 | S7 | Worker needs DDL privileges | 2.48 | 2.28 | 1.30 | 11.4 | 4.5 | 2.54 | 0.28 | ✅ |
| 10 | B1 | Worker stalls silently after a long DB outage | 2.91 | 1.75 | 1.45 | 10.8 | 5.3 | 2.05 | 0.53 | ❌ |
| 11 | O1 | DLQ has no alerting | 2.98 | 2.32 | 1.81 | 13.2 | 7.1 | 1.87 | 0.44 | ❌ |
| 12 | S6 | No field length/format limits | 2.80 | 1.35 | 1.48 | 8.9 | 5.4 | 1.65 | 0.36 | ❌ |
| 13 | O3 | Missing failure-path tests | 2.92 | 2.48 | 2.16 | 13.6 | 9.9 | 1.38 | 0.50 | ❌ |
| 14 | S3 | Version-freezing attack by any publisher | 2.99 | 1.29 | 1.89 | 9.1 | 7.5 | 1.23 | 0.54 | ❌ |

**Jev's 50% set:** S2, S4, S1, B4, B3, B2, O2, S5, S7. This uses 26.1h of the 30.6h budget.

## Caveats

- **Impact scores barely differ.** Jev gave 12 of the 14 findings an impact of 2.8–3.0, including dev-only S2 and test-tool B3. So the ranking comes mostly from effort, which favours cheap fixes regardless of where they matter.
- **Confidence is low overall** (0.0–0.61). For S5 it is 0.0, meaning the probability was spread across levels.
- **B1 is left out,** even though it is the only finding that silently stops all message processing.
- **Effort and likelihood scores match a manual reading of the findings** and are the more useful part of this ranking.

## Adjusted recommendation

This keeps Jev's effort and likelihood scores but corrects the flat impact scores. It swaps B3, B4 and S7 for B1 and O1, and uses about 30.5h of the 30.6h budget:

| Order | ID | ~Hours | Reason |
|---|---|---|---|
| 1 | B1 | 5.3 | Only finding that silently stops all message processing |
| 2 | B2 | 3.4 | Likely trigger; one bad deploy dead-letters the whole queue |
| 3 | O2 | 4.6 | Makes B1-type stalls visible; build it together with B1 |
| 4 | S1 | 2.7 | Cheap, and a forgotten env override is a common mistake |
| 5 | S4 | 1.2 | A few lines; closes the SQL injection path |
| 6 | S2 | 1.1 | Trivial |
| 7 | S5 | 5.2 | PII in transit; highest likelihood among the security findings |
| 8 | O1 | 7.1 | Messages dead-lettered by B1/B2 would otherwise go unnoticed |

**Deferred:**
- **O3** (tests): write only the tests for B1 and B2, as part of those fixes.
- **S3** (version-freezing attack): only matters once there are untrusted publishers.
- **S6** (field limits) and **S7** (separate DDL role): medium and low urgency.
- **B3** (test client seed) and **B4** (minor issues): low impact.
