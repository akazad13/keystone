import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Info, Loader2, Search, Shield } from "lucide-react";
import { Forbidden } from "@/components/auth/Forbidden";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { api, ApiError } from "@/lib/api";
import type { AdminUser } from "@/lib/schemas";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/users")({ component: UsersPage });

function RoleBadge({ role }: { role: string }) {
  const isAdmin = role === "Admin";
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium",
        isAdmin ? "bg-purple-400/10 text-purple-400" : "bg-admin-600/30 text-admin-300"
      )}
    >
      {isAdmin && <Shield className="h-3 w-3" />}
      {role}
    </span>
  );
}

function UsersPage() {
  const [searchQuery, setSearchQuery] = useState("");

  // GET /api/keystone-admin/admin/users -> BFF -> GET /api/admin/users (Admin role required).
  const users = useQuery<AdminUser[], ApiError>({
    queryKey: ["admin", "users"],
    queryFn: () => api.get<AdminUser[]>("/admin/users"),
    retry: false,
  });

  if (users.error?.status === 403) {
    return <Forbidden />;
  }

  const query = searchQuery.toLowerCase();
  const filtered = (users.data ?? []).filter(
    (u) => u.email.toLowerCase().includes(query) || u.displayName.toLowerCase().includes(query)
  );

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-white">Users</h2>
        <p className="mt-1 text-sm text-admin-400">Everyone who has used Keystone, as recorded by the Core API.</p>
      </div>

      <div className="flex items-start gap-3 rounded-lg border border-admin-700 bg-admin-800/60 p-4 text-sm text-admin-300">
        <Info className="mt-0.5 h-4 w-4 shrink-0 text-accent-400" />
        <p>
          Accounts, passwords and roles are managed in Zitadel, not here. A user appears in this list after their
          first API call, and their role is refreshed from their token on every call.
        </p>
      </div>

      <Card>
        <CardContent className="p-4">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-500" />
            <Input
              placeholder="Search users by name or email..."
              className="pl-10"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Users ({filtered.length})</CardTitle>
        </CardHeader>
        <CardContent className="p-0">
          {users.isLoading ? (
            <div className="flex items-center justify-center py-12">
              <Loader2 className="h-8 w-8 animate-spin text-accent-400" />
            </div>
          ) : users.isError ? (
            <p className="p-6 text-sm text-red-400">Could not load users ({users.error.status}).</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>User</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Projects</TableHead>
                  <TableHead>First seen</TableHead>
                  <TableHead>Last seen</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {filtered.map((user) => (
                  <TableRow key={user.id}>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent-600/20 text-xs font-bold text-accent-400">
                          {(user.displayName || user.email).charAt(0).toUpperCase()}
                        </div>
                        <div>
                          <p className="font-medium text-admin-100">{user.displayName}</p>
                          <p className="text-xs text-admin-400">{user.email}</p>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell>
                      <RoleBadge role={user.role} />
                    </TableCell>
                    <TableCell className="text-admin-300">{user.projectCount}</TableCell>
                    <TableCell className="text-admin-400">{new Date(user.createdAt).toLocaleDateString()}</TableCell>
                    <TableCell className="text-admin-400">{new Date(user.lastSeenAt).toLocaleString()}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
