# Demo Sourcing Manager Data

This seeder adds one synthetic tender at the Sourcing Manager stage, a submitted full-evaluation report, a recommended supplier and bid, and four Top Committee members. It does not delete or update existing data.

It requires `DEMO_SEED_CONNECTION` and `DEMO_MANAGER_PASSWORD` at runtime. The seed is idempotent: after the first run it reports that the demo report already exists.
