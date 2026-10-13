import { ShieldAlert } from "lucide-react";

/**
 * Shown when the Core API answers 403. The UI does not decide who is an admin: it asks the API,
 * and the API decides from the "role" claim in the Keystone token.
 */
export function Forbidden() {
  return (
    <div className="mx-auto mt-16 flex max-w-md flex-col items-center gap-4 rounded-xl border border-admin-700 bg-admin-800 p-10 text-center">
      <ShieldAlert className="h-10 w-10 text-amber-400" />
      <h2 className="text-lg font-bold text-white">Admins only</h2>
      <p className="text-sm text-admin-400">
        You are signed in, but your account does not have the Admin role. Roles are assigned in Zitadel
        (Users &gt; your user &gt; Authorizations); a change applies within 15 minutes, or immediately after
        signing in again.
      </p>
    </div>
  );
}
