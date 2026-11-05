# Local Kubernetes API exercise

The raw manifests below and the Helm chart in [../helm/gameserver-api](../helm/gameserver-api) are two ways to deploy the same API exercise. Redis and PostgreSQL are intentionally absent; game endpoints that use them will fail. Use no real players or production credentials here.

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

## Helm chart

Use this from the repository root in PowerShell. The chart creates a separate Deployment, ConfigMap and Service named `gameserver-api-helm`, so it can coexist with the raw `gameserver-api` resources above. It uses the existing `gameserver-api-local` Secret in the same namespace. The chart does not create a Namespace or store credentials in Helm values.

Confirm that the context is `kind-gameserver-local` and the Secret exists. If the Secret is missing, create it using the commands above.

```powershell
kubectl config current-context
kubectl -n gameserver-local get secret gameserver-api-local
helm lint ./k8s/helm/gameserver-api
helm template gameserver-api-helm ./k8s/helm/gameserver-api -n gameserver-local
```

Install the chart and inspect its resources:

```powershell
helm install gameserver-api-helm ./k8s/helm/gameserver-api -n gameserver-local --wait --timeout 3m
kubectl -n gameserver-local get deployment,service,configmap -l app.kubernetes.io/instance=gameserver-api-helm
helm list -n gameserver-local
```

In one PowerShell window, forward the Helm Service to a different local port than the raw Service uses:

```powershell
kubectl -n gameserver-local port-forward service/gameserver-api-helm 18081:8080
```

In a second window:

```powershell
curl.exe http://localhost:18081/ping
```

Expected response: `pong`. The defaults are in [../helm/gameserver-api/values.yaml](../helm/gameserver-api/values.yaml). Change the image tag for a release, then roll back to revision 1:

```powershell
helm upgrade gameserver-api-helm ./k8s/helm/gameserver-api -n gameserver-local --set image.tag=sha-f8e956b --wait --timeout 3m
helm history gameserver-api-helm -n gameserver-local
helm rollback gameserver-api-helm 1 -n gameserver-local --wait --timeout 3m
```

`--set image.tag` overrides only that value for the upgrade. Helm keeps release revisions, so rollback returns to the previous image and configuration. This exercise remains local: do not run the chart against Hetzner or Railway. `/ping` confirms that the HTTP process responds, not that Redis or PostgreSQL are usable.
