#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
BASE_URL=${BASE_URL:-http://127.0.0.1:5097}
DB_HOST=${DB_HOST:-127.0.0.1}
DB_PORT=${DB_PORT:-3306}
DB_USER=${DB_USER:-admin}
DB_NAME=${DB_NAME:-UKK}
: "${MYSQL_PWD:?MYSQL_PWD must be set}"

TMP_DIR=$(mktemp -d)
BODY_FILE="$TMP_DIR/body.json"
MARKER="e2e_$(date +%s)_$$"
APP_LOG="/tmp/opencode/ukk2026-${MARKER}.log"
APP_PID=""
UPLOADED_IMAGE=""
PASS_COUNT=0

ADMIN_USERNAME="${MARKER}_admin"
STAFF_USERNAME="${MARKER}_staff"
BORROWER_USERNAME="${MARKER}_borrower"
MANAGED_USERNAME="${MARKER}_managed"
MANAGED_UPDATED_USERNAME="${MARKER}_managed2"
PASSWORD="Test!12345"
CAPTCHA_TOKEN="automated-test-token"
STAMP=$(date +%s)
PHONE_SUFFIX=${STAMP: -9}
ADMIN_PHONE="081${PHONE_SUFFIX}01"
STAFF_PHONE="081${PHONE_SUFFIX}02"
BORROWER_PHONE="081${PHONE_SUFFIX}03"
MANAGED_PHONE="081${PHONE_SUFFIX}04"
MANAGED_UPDATED_PHONE="081${PHONE_SUFFIX}05"
CATEGORY_NAME="${MARKER}_kategori"
CATEGORY_UPDATED_NAME="${MARKER}_kategori2"
ALAT_NAME="${MARKER}_alat"
ALAT_UPDATED_NAME="${MARKER}_alat2"
UNIT_CODE="${MARKER}_unit"
UNIT_UPDATED_CODE="${MARKER}_unit2"
CUSTOM_STATUS="${MARKER}_custom"
CUSTOM_STATUS_UPDATED="${MARKER}_custom2"

db()
{
    mysql \
        --protocol=TCP \
        --host="$DB_HOST" \
        --port="$DB_PORT" \
        --user="$DB_USER" \
        --database="$DB_NAME" \
        --batch \
        --skip-column-names \
        "$@"
}

cleanup()
{
    set +e

    if [[ -n "$APP_PID" ]] && kill -0 "$APP_PID" 2>/dev/null; then
        kill "$APP_PID" 2>/dev/null
        wait "$APP_PID" 2>/dev/null
    fi

    db --execute="
        DELETE dpr
        FROM detail_pengembalian dpr
        INNER JOIN pengembalian pr ON pr.id = dpr.id_pengembalian
        INNER JOIN peminjaman p ON p.id = pr.id_peminjaman
        INNER JOIN users u ON u.id = p.id_peminjam
        WHERE u.username REGEXP '^${MARKER}';

        DELETE pr
        FROM pengembalian pr
        INNER JOIN peminjaman p ON p.id = pr.id_peminjaman
        INNER JOIN users u ON u.id = p.id_peminjam
        WHERE u.username REGEXP '^${MARKER}';

        DELETE dup
        FROM detail_unit_peminjaman dup
        INNER JOIN detail_peminjaman dp ON dp.id = dup.id_detail_peminjaman
        INNER JOIN peminjaman p ON p.id = dp.id_peminjaman
        INNER JOIN users u ON u.id = p.id_peminjam
        WHERE u.username REGEXP '^${MARKER}';

        DELETE dp
        FROM detail_peminjaman dp
        INNER JOIN peminjaman p ON p.id = dp.id_peminjaman
        INNER JOIN users u ON u.id = p.id_peminjam
        WHERE u.username REGEXP '^${MARKER}';

        DELETE p
        FROM peminjaman p
        INNER JOIN users u ON u.id = p.id_peminjam
        WHERE u.username REGEXP '^${MARKER}';

        DELETE ua
        FROM unit_alat ua
        INNER JOIN alat a ON a.id = ua.id_alat
        INNER JOIN kategori k ON k.id = a.id_kategori
        WHERE k.nama REGEXP '^${MARKER}';

        DELETE a
        FROM alat a
        INNER JOIN kategori k ON k.id = a.id_kategori
        WHERE k.nama REGEXP '^${MARKER}';

        DELETE FROM kategori WHERE nama REGEXP '^${MARKER}';
        DELETE FROM log_aktivitas
        WHERE id_user IN (SELECT id FROM users WHERE username REGEXP '^${MARKER}')
           OR aktivitas LIKE CONCAT('%', '${MARKER}', '%');
        DELETE FROM users WHERE username REGEXP '^${MARKER}';
        DELETE FROM status_barang WHERE nama REGEXP '^${MARKER}';
    " >/dev/null 2>&1

    if [[ "$UPLOADED_IMAGE" == /uploads/alat/* ]]; then
        rm -f "$ROOT_DIR/wwwroot$UPLOADED_IMAGE"
    fi

    rm -rf "$TMP_DIR"
}

trap cleanup EXIT

fail()
{
    printf 'FAIL: %s\n' "$1" >&2
    printf 'Response: %s\n' "${LAST_BODY:-<none>}" >&2
    printf 'Application log: %s\n' "$APP_LOG" >&2
    exit 1
}

request()
{
    local expected=$1
    local method=$2
    local path=$3
    local token=${4:-}
    local payload=${5:-}
    local args=(--silent --show-error --request "$method" --output "$BODY_FILE" --write-out '%{http_code}')

    if [[ -n "$token" ]]; then
        args+=(--header "Authorization: Bearer $token")
    fi

    if [[ -n "$payload" ]]; then
        args+=(--header 'Content-Type: application/json' --data "$payload")
    fi

    local status
    status=$(curl "${args[@]}" "$BASE_URL$path")
    LAST_BODY=$(<"$BODY_FILE")

    if [[ "$status" != "$expected" ]]; then
        fail "$method $path expected HTTP $expected, received $status"
    fi

    PASS_COUNT=$((PASS_COUNT + 1))
    printf 'PASS %s %s -> %s\n' "$method" "$path" "$status"
}

request_multipart()
{
    local expected=$1
    local method=$2
    local path=$3
    local token=$4
    shift 4
    local status
    status=$(curl \
        --silent \
        --show-error \
        --request "$method" \
        --header "Authorization: Bearer $token" \
        --output "$BODY_FILE" \
        --write-out '%{http_code}' \
        "$@" \
        "$BASE_URL$path")
    LAST_BODY=$(<"$BODY_FILE")

    if [[ "$status" != "$expected" ]]; then
        fail "$method $path expected HTTP $expected, received $status"
    fi

    PASS_COUNT=$((PASS_COUNT + 1))
    printf 'PASS %s %s -> %s\n' "$method" "$path" "$status"
}

request_file()
{
    local expected=$1
    local path=$2
    local output=$3
    local status
    status=$(curl \
        --silent \
        --show-error \
        --output "$output" \
        --write-out '%{http_code}' \
        "$BASE_URL$path")

    if [[ "$status" != "$expected" ]]; then
        LAST_BODY="<binary response>"
        fail "GET $path expected HTTP $expected, received $status"
    fi

    PASS_COUNT=$((PASS_COUNT + 1))
    printf 'PASS GET %s -> %s\n' "$path" "$status"
}

assert_json()
{
    local description=${!#}
    local jq_arg_count=$(( $# - 1 ))
    local jq_args=("${@:1:$jq_arg_count}")

    if ! jq --exit-status "${jq_args[@]}" <<<"$LAST_BODY" >/dev/null; then
        fail "$description"
    fi
}

json_object()
{
    jq --null-input --compact-output "$@"
}

cd "$ROOT_DIR"
dotnet build --no-restore >/dev/null

ASPNETCORE_ENVIRONMENT=Development \
    dotnet run --no-build --no-launch-profile --urls "$BASE_URL" >"$APP_LOG" 2>&1 &
APP_PID=$!

for _ in $(seq 1 30); do
    if curl --silent --fail "$BASE_URL/swagger/v1/swagger.json" >/dev/null 2>&1; then
        break
    fi
    sleep 1
done

if ! curl --silent --fail "$BASE_URL/swagger/v1/swagger.json" >/dev/null; then
    fail "API did not become ready"
fi

printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=' \
    | base64 --decode >"$TMP_DIR/test.png"

request 401 GET /api/v1/auth/me
request 200 GET /api/v1/auth/captcha-config
assert_json '.version == "v2" and .siteKey == "6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI"' \
    "Development reCAPTCHA v2 configuration is incorrect"

for account in admin staff borrower; do
    username_var="${account^^}_USERNAME"
    phone_var="${account^^}_PHONE"
    payload=$(json_object \
        --arg username "${!username_var}" \
        --arg nama "E2E ${account}" \
        --arg password "$PASSWORD" \
        --arg noTelepon "${!phone_var}" \
        '{username: $username, nama: $nama, password: $password, noTelepon: $noTelepon, alamat: "Alamat E2E"}')
    request 201 POST /api/v1/auth/register "" "$payload"
done

ADMIN_ROLE_ID=$(db --execute="SELECT id FROM roles WHERE nama = 'Admin'")
STAFF_ROLE_ID=$(db --execute="SELECT id FROM roles WHERE nama = 'Petugas'")
BORROWER_ROLE_ID=$(db --execute="SELECT id FROM roles WHERE nama = 'Peminjam'")
db --execute="
    UPDATE users SET id_role = ${ADMIN_ROLE_ID} WHERE username = '${ADMIN_USERNAME}';
    UPDATE users SET id_role = ${STAFF_ROLE_ID} WHERE username = '${STAFF_USERNAME}';
" >/dev/null

payload=$(json_object --arg username "$ADMIN_USERNAME" --arg password "$PASSWORD" \
    '{username: $username, password: $password, captchaToken: ""}')
request 400 POST /api/v1/auth/login "" "$payload"

LONG_CAPTCHA=$(printf 'x%.0s' $(seq 1 4097))
payload=$(json_object --arg username "$ADMIN_USERNAME" --arg password "$PASSWORD" --arg captcha "$LONG_CAPTCHA" \
    '{username: $username, password: $password, captchaToken: $captcha}')
request 400 POST /api/v1/auth/login "" "$payload"

payload=$(json_object --arg username "$ADMIN_USERNAME" --arg password "$PASSWORD" --arg captcha "$CAPTCHA_TOKEN" \
    '{username: $username, password: $password, captchaToken: $captcha}')
request 200 POST /api/v1/auth/login "" "$payload"
ADMIN_TOKEN=$(jq --raw-output '.token' <<<"$LAST_BODY")
assert_json '.user.role == "Admin"' "Admin login did not return the Admin role"

payload=$(json_object --arg username "$ADMIN_USERNAME" --arg captcha "$CAPTCHA_TOKEN" \
    '{username: $username, password: "wrong-password", captchaToken: $captcha}')
request 401 POST /api/v1/auth/login "" "$payload"

payload=$(json_object --arg username "$STAFF_USERNAME" --arg password "$PASSWORD" --arg captcha "$CAPTCHA_TOKEN" \
    '{username: $username, password: $password, captchaToken: $captcha}')
request 200 POST /api/v1/auth/login "" "$payload"
STAFF_TOKEN=$(jq --raw-output '.token' <<<"$LAST_BODY")
assert_json '.user.role == "Petugas"' "Staff login did not return the Petugas role"

payload=$(json_object --arg username "$BORROWER_USERNAME" --arg password "$PASSWORD" --arg captcha "$CAPTCHA_TOKEN" \
    '{username: $username, password: $password, captchaToken: $captcha}')
request 200 POST /api/v1/auth/login "" "$payload"
BORROWER_TOKEN=$(jq --raw-output '.token' <<<"$LAST_BODY")
assert_json '.user.role == "Peminjam"' "Borrower login did not return the Peminjam role"

request 200 GET /api/v1/auth/me "$ADMIN_TOKEN"
assert_json --arg username "$ADMIN_USERNAME" '.username == $username' "Authenticated profile is incorrect"
request 403 GET /api/v1/users "$BORROWER_TOKEN"
request 200 GET '/api/v1/users?page=1&pageSize=50' "$ADMIN_TOKEN"
assert_json '.total >= 3' "Admin user listing omitted registered users"

payload=$(json_object \
    --argjson idRole "$BORROWER_ROLE_ID" \
    --arg username "$MANAGED_USERNAME" \
    --arg nama 'Managed User' \
    --arg password "$PASSWORD" \
    --arg noTelepon "$MANAGED_PHONE" \
    '{idRole: $idRole, username: $username, nama: $nama, password: $password, noTelepon: $noTelepon, alamat: "Managed Address"}')
request 200 POST /api/v1/users "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/users?search=$MANAGED_USERNAME" "$ADMIN_TOKEN"
MANAGED_ID=$(jq --raw-output --arg username "$MANAGED_USERNAME" '.items[] | select(.username == $username) | .id' <<<"$LAST_BODY")
[[ -n "$MANAGED_ID" ]] || fail "Managed user was not returned"

payload=$(json_object \
    --argjson idRole "$BORROWER_ROLE_ID" \
    --arg username "$MANAGED_UPDATED_USERNAME" \
    --arg nama 'Managed User Updated' \
    --arg noTelepon "$MANAGED_UPDATED_PHONE" \
    '{idRole: $idRole, username: $username, nama: $nama, noTelepon: $noTelepon, alamat: "Updated Address"}')
request 200 PUT "/api/v1/users/$MANAGED_ID" "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/users/$MANAGED_ID" "$ADMIN_TOKEN"
assert_json --arg username "$MANAGED_UPDATED_USERNAME" '.username == $username' "Managed user update was not persisted"
request 200 DELETE "/api/v1/users/$MANAGED_ID" "$ADMIN_TOKEN"
request 404 GET "/api/v1/users/$MANAGED_ID" "$ADMIN_TOKEN"

request 200 GET /api/v1/status-barang "$ADMIN_TOKEN"
GOOD_STATUS_ID=$(jq --raw-output '.[] | select(.nama == "baik") | .id' <<<"$LAST_BODY")
HEAVY_STATUS_ID=$(jq --raw-output '.[] | select(.nama == "rusak_berat") | .id' <<<"$LAST_BODY")
[[ -n "$GOOD_STATUS_ID" && -n "$HEAVY_STATUS_ID" ]] || fail "Seeded status_barang values are missing"

payload=$(json_object --arg nama "$CUSTOM_STATUS" '{nama: $nama}')
request 200 POST /api/v1/status-barang "$ADMIN_TOKEN" "$payload"
request 200 GET /api/v1/status-barang "$ADMIN_TOKEN"
CUSTOM_STATUS_ID=$(jq --raw-output --arg nama "$CUSTOM_STATUS" '.[] | select(.nama == $nama) | .id' <<<"$LAST_BODY")
payload=$(json_object --arg nama "$CUSTOM_STATUS_UPDATED" '{nama: $nama}')
request 200 PATCH "/api/v1/status-barang/$CUSTOM_STATUS_ID" "$ADMIN_TOKEN" "$payload"
request 409 DELETE "/api/v1/status-barang/$GOOD_STATUS_ID" "$ADMIN_TOKEN"
request 200 DELETE "/api/v1/status-barang/$CUSTOM_STATUS_ID" "$ADMIN_TOKEN"

payload=$(json_object --arg nama "$CATEGORY_NAME" '{nama: $nama}')
request 200 POST /api/v1/kategori "$ADMIN_TOKEN" "$payload"
request 200 GET /api/v1/kategori "$ADMIN_TOKEN"
CATEGORY_ID=$(jq --raw-output --arg nama "$CATEGORY_NAME" '.[] | select(.nama == $nama) | .id' <<<"$LAST_BODY")
[[ -n "$CATEGORY_ID" ]] || fail "Created category was not returned"
payload=$(json_object --arg nama "$CATEGORY_UPDATED_NAME" '{nama: $nama}')
request 200 PUT "/api/v1/kategori/$CATEGORY_ID" "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/kategori/$CATEGORY_ID" "$ADMIN_TOKEN"
assert_json --arg nama "$CATEGORY_UPDATED_NAME" '.nama == $nama' "Category update was not persisted"

request 415 POST /api/v1/alat "$ADMIN_TOKEN" '{}'
request_multipart 200 POST /api/v1/alat "$ADMIN_TOKEN" \
    --form "idKategori=$CATEGORY_ID" \
    --form "nama=$ALAT_NAME" \
    --form 'deskripsi=Alat pengujian E2E' \
    --form 'hargaSewa=12500.50' \
    --form "gambar=@$TMP_DIR/test.png;type=image/png"
request 200 GET "/api/v1/alat?search=$ALAT_NAME" "$ADMIN_TOKEN"
ALAT_ID=$(jq --raw-output --arg nama "$ALAT_NAME" '.items[] | select(.nama == $nama) | .id' <<<"$LAST_BODY")
UPLOADED_IMAGE=$(jq --raw-output --arg nama "$ALAT_NAME" '.items[] | select(.nama == $nama) | .imagePath' <<<"$LAST_BODY")
[[ -n "$ALAT_ID" && "$UPLOADED_IMAGE" == /uploads/alat/* ]] || fail "Alat or uploaded image path was not returned"
[[ -f "$ROOT_DIR/wwwroot$UPLOADED_IMAGE" ]] || fail "Uploaded image was not written to the web root"
request_file 200 "$UPLOADED_IMAGE" "$TMP_DIR/downloaded.png"
cmp --silent "$TMP_DIR/test.png" "$TMP_DIR/downloaded.png" || fail "Served image differs from the uploaded file"

request_multipart 200 PUT "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN" \
    --form "idKategori=$CATEGORY_ID" \
    --form "nama=$ALAT_UPDATED_NAME" \
    --form 'deskripsi=Alat pengujian E2E diperbarui' \
    --form 'hargaSewa=15000.00'
request 200 GET "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
assert_json --arg nama "$ALAT_UPDATED_NAME" --arg image "$UPLOADED_IMAGE" \
    '.nama == $nama and .imagePath == $image and .hargaSewa == 15000' \
    "Alat update or image retention failed"

AVAILABLE_STATUS_ID=$(db --execute="SELECT id FROM status_ketersediaan WHERE nama = 'tersedia'")
payload=$(json_object \
    --argjson idAlat "$ALAT_ID" \
    --arg kode "$UNIT_CODE" \
    --argjson idStatusBarang "$GOOD_STATUS_ID" \
    '{idAlat: $idAlat, kodeInventaris: $kode, idStatusBarang: $idStatusBarang}')
request 200 POST /api/v1/unit-alat "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/unit-alat?idAlat=$ALAT_ID" "$ADMIN_TOKEN"
UNIT_ID=$(jq --raw-output --arg kode "$UNIT_CODE" '.items[] | select(.kodeInventaris == $kode) | .id' <<<"$LAST_BODY")
[[ -n "$UNIT_ID" ]] || fail "Created unit was not returned"
payload=$(json_object \
    --arg kode "$UNIT_UPDATED_CODE" \
    --argjson idStatusBarang "$GOOD_STATUS_ID" \
    --argjson idStatusKetersediaan "$AVAILABLE_STATUS_ID" \
    '{kodeInventaris: $kode, idStatusBarang: $idStatusBarang, idStatusKetersediaan: $idStatusKetersediaan}')
UNIT_UPDATE_PAYLOAD=$payload
request 200 PUT "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN"
assert_json --arg kode "$UNIT_UPDATED_CODE" '.kodeInventaris == $kode and .statusKetersediaan == "tersedia"' \
    "Unit update was not persisted"
request 200 GET "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
assert_json '.stokTersedia == 1' "Available stock did not increase after unit creation"
request 409 DELETE "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
request 409 DELETE "/api/v1/kategori/$CATEGORY_ID" "$ADMIN_TOKEN"

TODAY=$(date +%F)
PLANNED_RETURN=$(date -d '+2 days' +%F)
UPDATED_PLANNED_RETURN=$(date -d '+3 days' +%F)
payload=$(json_object \
    --arg start "$TODAY" \
    --arg finish "$PLANNED_RETURN" \
    '{tanggalPeminjaman: $start, tanggalRencanaKembali: $finish, items: []}')
request 400 POST /api/v1/peminjaman "$BORROWER_TOKEN" "$payload"

payload=$(json_object \
    --arg start "$TODAY" \
    --arg finish "$PLANNED_RETURN" \
    --argjson idAlat "$ALAT_ID" \
    '{tanggalPeminjaman: $start, tanggalRencanaKembali: $finish, items: [{idAlat: $idAlat, jumlah: 1}]}')
request 200 POST /api/v1/peminjaman "$BORROWER_TOKEN" "$payload"
request 200 GET '/api/v1/peminjaman/mine?page=1&pageSize=20' "$BORROWER_TOKEN"
LOAN_ID=$(jq --raw-output --argjson idAlat "$ALAT_ID" '.items[] | select(.details[0].idAlat == $idAlat) | .id' <<<"$LAST_BODY")
[[ -n "$LOAN_ID" ]] || fail "Created peminjaman was not returned"
assert_json --argjson id "$LOAN_ID" '.items[] | select(.id == $id) | .status == "diajukan" and .totalBiaya == 45000' \
    "Peminjaman snapshot price or inclusive rental total is incorrect"

request 403 GET /api/v1/peminjaman "$BORROWER_TOKEN"
request 200 GET /api/v1/peminjaman "$ADMIN_TOKEN"
assert_json --argjson id "$LOAN_ID" '.items | any(.id == $id)' "Admin loan listing omitted the test loan"
payload=$(json_object \
    --arg start "$TODAY" \
    --arg finish "$UPDATED_PLANNED_RETURN" \
    --argjson idAlat "$ALAT_ID" \
    '{tanggalPeminjaman: $start, tanggalRencanaKembali: $finish, items: [{idAlat: $idAlat, jumlah: 1}]}')
request 200 PUT "/api/v1/peminjaman/$LOAN_ID" "$ADMIN_TOKEN" "$payload"
request 200 GET "/api/v1/peminjaman/$LOAN_ID" "$BORROWER_TOKEN"
assert_json --arg finish "$UPDATED_PLANNED_RETURN" \
    '.tanggalRencanaKembali == $finish and .totalBiaya == 60000' \
    "Peminjaman update did not recalculate the rental snapshot total"
request 200 PATCH "/api/v1/peminjaman/$LOAN_ID/approve" "$ADMIN_TOKEN"
request 200 PATCH "/api/v1/peminjaman/$LOAN_ID/borrow" "$STAFF_TOKEN"
request 200 GET "/api/v1/peminjaman/$LOAN_ID" "$BORROWER_TOKEN"
assert_json --arg kode "$UNIT_UPDATED_CODE" '.status == "dipinjam" and .details[0].kodeInventaris[0] == $kode' \
    "Loan handover did not assign the inventory unit"
request 200 GET '/api/v1/peminjaman/monitor?status=dipinjam' "$STAFF_TOKEN"
assert_json --argjson id "$LOAN_ID" '.items | any(.id == $id)' "Borrowed loan was absent from monitoring"
request 409 PUT "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN" "$UNIT_UPDATE_PAYLOAD"
request 409 DELETE "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN"
request 200 GET "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
assert_json '.stokTersedia == 0' "Borrowed unit remained in available stock"

RETURN_CONDITION_ID=$(db --execute="SELECT id FROM status_kondisi_kembali WHERE nama = 'baik'")
payload=$(json_object \
    --argjson idPeminjaman "$LOAN_ID" \
    --arg date "$TODAY" \
    --argjson idUnit "$UNIT_ID" \
    --argjson idCondition "$RETURN_CONDITION_ID" \
    '{idPeminjaman: $idPeminjaman, tanggalKembali: $date, catatan: "Pengembalian E2E", units: [{idUnitAlat: $idUnit, idStatusKondisiKembali: $idCondition, catatan: "Baik"}]}')
request 403 POST /api/v1/pengembalian "$BORROWER_TOKEN" "$payload"
request 200 POST /api/v1/pengembalian "$STAFF_TOKEN" "$payload"
request 200 GET '/api/v1/pengembalian?page=1&pageSize=20' "$STAFF_TOKEN"
RETURN_ID=$(jq --raw-output --argjson loan "$LOAN_ID" '.items[] | select(.idPeminjaman == $loan) | .id' <<<"$LAST_BODY")
[[ -n "$RETURN_ID" ]] || fail "Created pengembalian was not returned"
assert_json --argjson id "$RETURN_ID" '.items[] | select(.id == $id) | .details[0].statusKondisiKembali == "baik" and .denda == 0' \
    "Return condition or fine is incorrect"
request 200 GET "/api/v1/pengembalian/$RETURN_ID" "$BORROWER_TOKEN"
request 200 GET "/api/v1/peminjaman/$LOAN_ID" "$BORROWER_TOKEN"
assert_json '.status == "selesai"' "Returned loan was not transitioned to selesai"
request 409 DELETE "/api/v1/peminjaman/$LOAN_ID" "$ADMIN_TOKEN"
request 200 GET "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
assert_json '.stokTersedia == 1' "Returned good unit did not return to available stock"

request 200 GET "/api/v1/laporan/peminjaman?from=$TODAY&to=$UPDATED_PLANNED_RETURN" "$STAFF_TOKEN"
assert_json --argjson id "$LOAN_ID" '.items | any(.idPeminjaman == $id)' "Loan report omitted the test loan"
request 200 GET "/api/v1/laporan/pengembalian?from=$TODAY&to=$UPDATED_PLANNED_RETURN" "$STAFF_TOKEN"
assert_json --argjson id "$RETURN_ID" '.items | any(.idPengembalian == $id)' "Return report omitted the test return"
request 200 GET "/api/v1/laporan/peminjaman?from=$TODAY&to=$UPDATED_PLANNED_RETURN&format=csv" "$STAFF_TOKEN"
[[ "$LAST_BODY" == IdPeminjaman* ]] || fail "Loan CSV report is missing its header"
request 200 GET "/api/v1/laporan/pengembalian?from=$TODAY&to=$UPDATED_PLANNED_RETURN&format=csv" "$STAFF_TOKEN"
[[ "$LAST_BODY" == IdPengembalian* ]] || fail "Return CSV report is missing its header"
request 200 GET '/api/v1/log-aktivitas?page=1&pageSize=100' "$ADMIN_TOKEN"
assert_json '.total > 0' "Activity log remained empty"

payload=$(json_object \
    --arg start "$TODAY" \
    --arg finish "$UPDATED_PLANNED_RETURN" \
    --argjson idAlat "$ALAT_ID" \
    '{tanggalPeminjaman: $start, tanggalRencanaKembali: $finish, items: [{idAlat: $idAlat, jumlah: 1}]}')
request 200 POST /api/v1/peminjaman "$BORROWER_TOKEN" "$payload"
request 200 GET '/api/v1/peminjaman/mine?page=1&pageSize=20' "$BORROWER_TOKEN"
REJECTED_LOAN_ID=$(jq --raw-output --argjson first "$LOAN_ID" \
    '[.items[] | select(.id != $first)] | max_by(.id) | .id' <<<"$LAST_BODY")
[[ -n "$REJECTED_LOAN_ID" && "$REJECTED_LOAN_ID" != "null" ]] || fail "Second peminjaman was not returned"
request 200 PATCH "/api/v1/peminjaman/$REJECTED_LOAN_ID/reject" "$STAFF_TOKEN"
request 200 GET "/api/v1/peminjaman/$REJECTED_LOAN_ID" "$BORROWER_TOKEN"
assert_json '.status == "ditolak"' "Rejected peminjaman did not transition to ditolak"
request 200 DELETE "/api/v1/peminjaman/$REJECTED_LOAN_ID" "$ADMIN_TOKEN"
request 404 GET "/api/v1/peminjaman/$REJECTED_LOAN_ID" "$BORROWER_TOKEN"

request 200 DELETE "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN"
request 404 GET "/api/v1/unit-alat/$UNIT_ID" "$ADMIN_TOKEN"
request 200 DELETE "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
request 404 GET "/api/v1/alat/$ALAT_ID" "$ADMIN_TOKEN"
request 200 DELETE "/api/v1/kategori/$CATEGORY_ID" "$ADMIN_TOKEN"
request 404 GET "/api/v1/kategori/$CATEGORY_ID" "$ADMIN_TOKEN"

request 200 POST /api/v1/auth/logout "$BORROWER_TOKEN"
request 200 POST /api/v1/auth/logout "$STAFF_TOKEN"
request 200 POST /api/v1/auth/logout "$ADMIN_TOKEN"

printf 'All %s end-to-end assertions passed. Marker: %s\n' "$PASS_COUNT" "$MARKER"
printf 'Application log: %s\n' "$APP_LOG"
