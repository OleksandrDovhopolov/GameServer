# Local Kubernetes API exercise

This deploys one GameServer API Pod for `/ping` only. Redis and PostgreSQL are intentionally absent; game endpoints that use them will fail. Use no real players or production credentials here.

Run these commands in PowerShell on Windows, from the repository root, with Docker Desktop and the `kind-gameserver-local` cluster running:

```powershell
kubectl config current-context
kubectl apply -f k8s/api-local/namespace.yaml
```

The context must be `kind-gameserver-local`. Create a Secret directly in the cluster; the generated values are only for this isolated exercise and are not stored in Git:

```powershell
$localRng = [Security.Cryptography.RandomNumberGenerator]::Create()
$localPasswordBytes = New-Object byte[] 24
$localJwtBytes = New-Object byte[] 32
$localRng.GetBytes($localPasswordBytes)
$localRng.GetBytes($localJwtBytes)
$localAdminPassword = [BitConverter]::ToString($localPasswordBytes).Replace('-', '')
$localJwtKey = [BitConverter]::ToString($localJwtBytes).Replace('-', '')
$localRng.Dispose()
kubectl -n gameserver-local create secret generic gameserver-api-local --from-literal=ADMIN_USER=local-admin --from-literal=ADMIN_PASS=$localAdminPassword --from-literal=Auth__JwtSigningKey=$localJwtKey
```

Apply the application resources and wait for the Pod:

```powershell
kubectl apply -f k8s/api-local/configmap.yaml -f k8s/api-local/deployment.yaml -f k8s/api-local/service.yaml
kubectl -n gameserver-local rollout status deployment/gameserver-api --timeout=180s
kubectl -n gameserver-local get pods,services
```

Keep this command running in its own PowerShell window:

```powershell
kubectl -n gameserver-local port-forward service/gameserver-api 18080:8080
```

In another window:

```powershell
curl.exe http://localhost:18080/ping
```

Expected response: `pong`. If the Pod does not start, inspect it with `kubectl -n gameserver-local describe pod -l app=gameserver-api` and `kubectl -n gameserver-local logs deployment/gameserver-api`. A green `/ping` probe checks only the HTTP process, not Redis or PostgreSQL.
