# hoodi Pet — imported source

Source: the supplied `hoodi-pet.zip` archive. Files under `hoodi-pet/` retain their original contents. This import brings the firmware, BLE protocol helpers, alert rules, tests and documentation into the team repository.

The `app/` directory contains integration modules, not a complete Expo application. Fernando owns firmware/risk integration and Mariana owns mobile/BLE integration; their original source documentation remains available in [the package README](hoodi-pet/README.md).

Validation performed: `node --experimental-strip-types --test iot/hoodi-pet/testes/app.test.ts` — 16 tests passed. These test protocol encoding and alert rules without native BLE or hardware.

Firmware compilation, flashing, native device BLE, battery/TP4056 assembly and physical enclosure decisions were not validated by this import. No toolchain or dependencies were installed. Historical assembly instructions in the source are not new hardware approvals.
