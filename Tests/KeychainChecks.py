#!/usr/bin/env python3
"""Compile the Keychain write path with stub Security calls. Access no Keychain items."""
from pathlib import Path
import subprocess
repo = Path(__file__).resolve().parents[1]
root = Path('/tmp/dictate-sol-fixes/keychain-checks')
root.mkdir(exist_ok=True)
source = (repo / 'Sources/CleanupClients.swift').read_text().split('enum Keychain {', 1)[1]
(root / 'Keychain.swift').write_text('import Foundation\nimport CoreFoundation\nenum Keychain {' + source)
(root / 'Checks.swift').write_text(r'''
import Foundation
import CoreFoundation
typealias OSStatus = Int32
let errSecSuccess: OSStatus = 0, errSecItemNotFound: OSStatus = -25300
let kSecClass = "class", kSecClassGenericPassword = "generic", kSecAttrService = "service"
let kSecAttrAccount = "account", kSecReturnData = "return", kSecValueData = "data"
var saved: Data? = Data("old generated key".utf8)
var failure: OSStatus = 0
var updates = 0, adds = 0, deletes = 0
func SecItemUpdate(_ query: CFDictionary, _ attributes: CFDictionary) -> OSStatus {
    updates += 1
    if failure != 0 { return failure }
    guard saved != nil else { return errSecItemNotFound }
    saved = (attributes as NSDictionary)[kSecValueData] as? Data
    return errSecSuccess
}
func SecItemAdd(_ attributes: CFDictionary, _ result: UnsafeMutablePointer<CFTypeRef?>?) -> OSStatus {
    adds += 1
    if failure != 0 { return failure }
    saved = (attributes as NSDictionary)[kSecValueData] as? Data
    return errSecSuccess
}
func SecItemDelete(_ query: CFDictionary) -> OSStatus {
    deletes += 1
    if failure != 0 { return failure }
    let status = saved == nil ? errSecItemNotFound : errSecSuccess
    saved = nil
    return status
}
func SecItemCopyMatching(_ query: CFDictionary, _ result: UnsafeMutablePointer<AnyObject?>?) -> OSStatus { errSecItemNotFound }
@main struct Checks {
    static func main() {
        let old = saved
        failure = -25293
        precondition(Keychain.write("test", Data("new".utf8)) == failure)
        precondition(saved == old && deletes == 0 && adds == 0 && updates == 1)
        failure = 0
        precondition(Keychain.write("test", Data("new".utf8)) == 0)
        precondition(saved == Data("new".utf8) && deletes == 0 && adds == 0)
        saved = nil
        precondition(Keychain.write("test", Data("new".utf8)) == 0 && adds == 1)
        precondition(Keychain.write("test", nil) == 0 && saved == nil)
        precondition(Keychain.write("test", nil) == 0)
        failure = -25293
        precondition(Keychain.write("test", nil) == failure)
        print("Passed credential update, retained-value failure, creation, deletion, and real-status checks with stub Security calls")
    }
}
''')
subprocess.run(['xcrun', 'swiftc', '-swift-version', '5', '-parse-as-library', str(root / 'Keychain.swift'),
                str(root / 'Checks.swift'), '-o', str(root / 'checks')], check=True)
subprocess.run([str(root / 'checks')], check=True)
