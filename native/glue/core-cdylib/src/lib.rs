use std::ffi::{c_char, CStr, CString};

#[no_mangle]
pub extern "C" fn tinadec_native_version() -> *mut c_char {
    into_c_string(r#"{"name":"tinadec-core-native","version":"0.1.0","upstream":"codex-rust-pending"}"#)
}

#[no_mangle]
pub extern "C" fn tinadec_guardian_check(payload: *const c_char) -> *mut c_char {
    let input = read_c_string(payload);
    let risk = if input.contains("apply_patch") || input.contains("sandbox_exec") || input.contains("shell") {
        "approval-required"
    } else {
        "allowed"
    };

    into_c_string(&format!(
        r#"{{"status":"stubbed","decision":"{}","source":"tinadec-core-native","upstream":"codex-rust-pending"}}"#,
        risk
    ))
}

#[no_mangle]
pub unsafe extern "C" fn tinadec_free_string(value: *mut c_char) {
    if value.is_null() {
        return;
    }

    let _ = CString::from_raw(value);
}

fn read_c_string(value: *const c_char) -> String {
    if value.is_null() {
        return String::new();
    }

    unsafe { CStr::from_ptr(value).to_string_lossy().into_owned() }
}

fn into_c_string(value: &str) -> *mut c_char {
    CString::new(value).expect("native JSON must not contain nul bytes").into_raw()
}
