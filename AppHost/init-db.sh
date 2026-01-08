#!/bin/bash
set -e

# Function to create a database
create_database() {
    local db_name=$1
    echo "Creating database: $db_name"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
        CREATE DATABASE $db_name;
EOSQL
    echo "Database $db_name created successfully"
}

# Create databases from POSTGRES_MULTIPLE_DATABASES environment variable
if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
    echo "Multiple databases requested: $POSTGRES_MULTIPLE_DATABASES"
    for db in $(echo $POSTGRES_MULTIPLE_DATABASES | tr ',' ' '); do
        if [ "$db" != "$POSTGRES_DB" ]; then
            create_database $db
        fi
    done
    echo "All databases created successfully"
else
    echo "No additional databases requested"
fi
