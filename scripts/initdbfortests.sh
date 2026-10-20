#!/bin/bash
# initdbfortests — Reset jobs table to clean state for ConsoleTester tests
# Runs DELETE FROM jobs against the PostgreSQL database.
# This is the expected initial condition for ConsoleTester tests.

set -e

# Try host psql first, fall back to Docker exec
if command -v psql &> /dev/null; then
    PGPASSWORD=job_queue_pass psql -h localhost -p 5432 -U job_queue_user -d job_queue_db -c "TRUNCATE jobs, job_attempts;"
else
    docker exec dist_job_queue psql -U job_queue_user -d job_queue_db -c "TRUNCATE jobs, job_attempts;"
fi