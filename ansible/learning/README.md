# Ansible lab on a disposable Ubuntu VM

This playbook is for a fresh Ubuntu VM. It does not target the public staging VPS.
It installs three packages, creates an unprivileged user, and writes a harmless config file.

Run these commands from an Ubuntu/WSL shell with Ansible installed:

```bash
cd ansible/learning
cp inventory.example.ini inventory.ini
# Edit inventory.ini: replace the example IP and SSH user with your temporary VM.
ansible -i inventory.ini lab -m ping
ansible-playbook -i inventory.ini site.yml --syntax-check
ansible-playbook -i inventory.ini site.yml --ask-become-pass
ansible-playbook -i inventory.ini site.yml --ask-become-pass
ansible-playbook -i inventory.ini site.yml --check --diff --ask-become-pass
```

Use `--private-key ~/.ssh/<vm-key>` if your SSH client does not select the VM key
automatically. `--ask-become-pass` asks for the VM user's sudo password; omit it if the VM
user has passwordless sudo. The second real run should report `changed=0`. `--check`
previews changes after the VM has been configured.

Verify on the VM with `id gameserverlab` and `cat /etc/gameserver-lab/settings.conf`.
The real inventory file is ignored by Git; commit only the example.
