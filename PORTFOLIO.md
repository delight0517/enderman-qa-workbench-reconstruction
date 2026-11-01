# Enderman Combat & Environment QA

**A 100-case test design and evidence-led coverage review for Minecraft Java Edition.**

> Independent QA portfolio case study. Not affiliated with Mojang or Microsoft. The Workbench download in this repository is a reconstructed recovery preview; it is not the original official source or a qualified production test runner.

## 프로젝트 소개

엔더맨의 전투·환경 반응을 100개의 식별 가능한 테스트 케이스로 나누고, 기대 결과와 실제 관측 범위를 구분해 정리한 QA 프로젝트입니다. 핵심 목표는 “Pass”라는 한 단어에 결과를 뭉뚱그리지 않고, 서버 텔레메트리로 확인한 사실과 클라이언트 플레이에서 아직 확인하지 못한 동작을 분리하는 것입니다.

## Scope and method

- 100 planned cases with stable IDs, preconditions, actions, expected behavior and pass criteria.
- Scenario areas include melee damage, damage-source attribution, death and drops, environmental hazards, water-cloud exposure, teleport behavior and client/server position consistency.
- Attempts record the adapter/capability used, observed coverage and a journal reference. Repeated attempts are retained rather than silently replacing earlier observations.
- The public case list includes only case ID, title, latest status, verdict and a short coverage note. It omits raw journals, machine paths, instance IDs, credentials and Jira account metadata.

## Current result snapshot — 2026-11-01

| Measure | Result |
|---|---:|
| Planned cases | 100 |
| Cases with a dispatched attempt | 99 |
| Full-coverage passes | 18 |
| Partial/bounded passes | 78 |
| Inconclusive | 3 |
| Not run | 1 |

These figures come from the latest-per-case reconciliation of the retained result records. The 78 partial/bounded outcomes are not full passes. A dispatched attempt is not necessarily an end-to-end player session: much of the existing evidence is controlled server-side telemetry. **The 100-case QA is incomplete.**

## What the review found

- Case #014 (stale-position attack after teleport) remains unrun because client attack input and authoritative-server reconciliation were not exercised.
- Case #060 (projectiles under latency) has a controlled server-thread projectile observation, but no real client bow input or transport latency; the full scenario remains inconclusive.
- Cases #074 and #079 cover water-cloud damage and teleport transitions. Existing observations do not establish the required pre-exit damage event, natural teleport selection or potion-effect membership.
- No gameplay screenshots or videos were captured, and no Jira issue was submitted. A Jira import file exists locally but is not included here.

This is intentional QA reporting: missing coverage is recorded as missing or inconclusive instead of being presented as a confirmed game defect or a complete pass.

## Public artifacts

- [Sanitized 100-case status and coverage CSV](portfolio/Enderman-100-Case-Results.csv)
- [Reconstructed Workbench source](https://github.com/delight0517/enderman-qa-workbench-reconstruction)
- [Windows x64 recovery preview 3](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/tag/v0.1.0-preview.3)

The preview is an experimental source-recovery build. Its package constructor smoke check and isolated-source UI render passed, but it has not passed full interactive startup review, a complete client gameplay run, complete suite execution or Jira submission.

## Career portfolio framing

This project demonstrates test decomposition, explicit expected results, coverage accounting, telemetry review, repeatability tracking and honest defect triage. The current strongest claim is a structured 100-case plan with 99 dispatched attempts and a reconciled set of partial and unresolved outcomes—not a completed 100-case gameplay certification.

## Trademark and source note

Minecraft and Enderman are used only to identify the independent test subject. This project is not endorsed by or affiliated with Mojang or Microsoft. Workbench source is a clearly labeled reconstruction from the installed 0.3.184 application because the original source was not recovered; its repository license does not imply rights to third-party game assets or marks.
