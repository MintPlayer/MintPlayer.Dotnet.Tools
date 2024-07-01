    $rootcert=(New-SelfSignedCertificate -DnsName "headers.mintplayer.com", "root_ca_dev_header.mintplayer.com" -CertStoreLocation "cert:\LocalMachine\My" -NotAfter (Get-Date).AddYears(20) -FriendlyName "root_ca_dev_headers.mintplayer.com" -KeyUsageProperty All -KeyUsage CertSign, CRLSign, DigitalSignature)
    $mypwd = ConvertTo-SecureString -String "1234" -Force -AsPlainText
    Get-ChildItem -Path cert:\localMachine\my\B0CC5D429E0A685F626EB8DCAC4719EC986A6453 | Export-PfxCertificate -FilePath ./root_ca_dev_headers.pfx -Password $mypwd
    Export-Certificate -Cert cert:\localMachine\my\B0CC5D429E0A685F626EB8DCAC4719EC986A6453 -FilePath root_ca_headers.crt
    
    $childcert=(New-SelfSignedCertificate -certstorelocation cert:\localmachine\my -dnsname "headers.mintplayer.com" -Signer $rootcert -NotAfter (Get-Date).AddYears(20) -FriendlyName "child_a_headers.mintplayer.com")
    $childcert | Export-PfxCertificate -FilePath ./child_a_headers.pfx -Password $mypwd
    $childcert | Export-Certificate -FilePath ./child_a_headers.crt

Postman => Settings => Certificates => headers.mintplayer.com / Select crt and pfx / passphrase 1234
