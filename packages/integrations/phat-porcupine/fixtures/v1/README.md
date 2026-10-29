# Catalog fixtures, version 1

Halcyonic wrote these by hand from Phat Porcupine's published catalog contract, version 1
(`shared/contract/consumer.ts`), to test its own client. They are not Phat Porcupine's fixtures and
copy nothing from them. Every project, owner, repository and person in them is invented.

- `catalog-small.json`: `GET /consumer/v1/catalog`, three projects of two invented owners, one with
  two repositories, one waiting on a review.
- `error-not-ready-503.json`: the answer before the first sync.
- `error-unauthorized-401.json`: the answer to a credential that is not valid.
