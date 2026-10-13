import { createFileRoute } from "@tanstack/react-router";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { api } from "@/lib/api";
import { type User, type UserListResponse, type CreateUserInput, createUserSchema } from "@/lib/schemas";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { UserPlus, Search, MoreHorizontal, Edit2, Trash2, Shield, X, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/users")({ component: UsersPage });

const mockUsers: User[] = [
  { id: "1", email: "admin@example.com", displayName: "System Admin", role: "admin", status: "active", createdAt: "2024-01-15T10:00:00Z", lastLogin: "2025-03-13T08:30:00Z" },
  { id: "2", email: "jane.smith@example.com", displayName: "Jane Smith", role: "manager", status: "active", createdAt: "2024-03-22T14:00:00Z", lastLogin: "2025-03-12T16:45:00Z" },
  { id: "3", email: "bob.wilson@example.com", displayName: "Bob Wilson", role: "user", status: "active", createdAt: "2024-06-01T09:00:00Z", lastLogin: "2025-03-10T11:20:00Z" },
  { id: "4", email: "alice.jones@example.com", displayName: "Alice Jones", role: "user", status: "inactive", createdAt: "2024-07-15T12:00:00Z", lastLogin: "2025-01-05T09:00:00Z" },
  { id: "5", email: "charlie.brown@example.com", displayName: "Charlie Brown", role: "user", status: "suspended", createdAt: "2024-09-10T08:00:00Z", lastLogin: null },
  { id: "6", email: "diana.prince@example.com", displayName: "Diana Prince", role: "manager", status: "active", createdAt: "2024-10-20T15:00:00Z", lastLogin: "2025-03-13T07:00:00Z" },
];

function StatusBadge({ status }: { status: User["status"] }) {
  return (
    <span className={cn("inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium", status === "active" && "bg-emerald-400/10 text-emerald-400", status === "inactive" && "bg-amber-400/10 text-amber-400", status === "suspended" && "bg-red-400/10 text-red-400")}>
      <span className={cn("mr-1.5 h-1.5 w-1.5 rounded-full", status === "active" && "bg-emerald-400", status === "inactive" && "bg-amber-400", status === "suspended" && "bg-red-400")} />
      {status.charAt(0).toUpperCase() + status.slice(1)}
    </span>
  );
}

function RoleBadge({ role }: { role: User["role"] }) {
  return (
    <span className={cn("inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs font-medium", role === "admin" && "bg-purple-400/10 text-purple-400", role === "manager" && "bg-blue-400/10 text-blue-400", role === "user" && "bg-admin-600/30 text-admin-300")}>
      {role === "admin" && <Shield className="h-3 w-3" />}
      {role.charAt(0).toUpperCase() + role.slice(1)}
    </span>
  );
}

function UsersPage() {
  const [searchQuery, setSearchQuery] = useState("");
  const [showCreateModal, setShowCreateModal] = useState(false);
  const [actionMenuId, setActionMenuId] = useState<string | null>(null);
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery<UserListResponse>({
    queryKey: ["users"],
    queryFn: () => api.get<UserListResponse>("/users"),
    placeholderData: { users: mockUsers, total: mockUsers.length, page: 1, pageSize: 20 },
  });

  const createMutation = useMutation({
    mutationFn: (input: CreateUserInput) => api.post("/users", input),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["users"] }); setShowCreateModal(false); },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => api.delete(`/users/${id}`),
    onSuccess: () => { queryClient.invalidateQueries({ queryKey: ["users"] }); },
  });

  const users = data?.users ?? [];
  const filteredUsers = users.filter((u) => u.email.toLowerCase().includes(searchQuery.toLowerCase()) || u.displayName.toLowerCase().includes(searchQuery.toLowerCase()));

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CreateUserInput>({
    resolver: zodResolver(createUserSchema),
    defaultValues: { role: "user" },
  });

  const onCreateUser = (formData: CreateUserInput) => { createMutation.mutate(formData); reset(); };

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-white">User Management</h2>
          <p className="mt-1 text-sm text-admin-400">Manage user accounts, roles, and permissions.</p>
        </div>
        <Button onClick={() => setShowCreateModal(true)}>
          <UserPlus className="mr-2 h-4 w-4" />
          Add User
        </Button>
      </div>

      <Card>
        <CardContent className="p-4">
          <div className="flex items-center gap-4">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-500" />
              <Input placeholder="Search users by name or email..." className="pl-10" value={searchQuery} onChange={(e) => setSearchQuery(e.target.value)} />
            </div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm">All</Button>
              <Button variant="ghost" size="sm">Active</Button>
              <Button variant="ghost" size="sm">Inactive</Button>
              <Button variant="ghost" size="sm">Suspended</Button>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle>Users ({filteredUsers.length})</CardTitle>
          </div>
        </CardHeader>
        <CardContent className="p-0">
          {isLoading ? (
            <div className="flex items-center justify-center py-12">
              <Loader2 className="h-8 w-8 animate-spin text-accent-400" />
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>User</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Created</TableHead>
                  <TableHead>Last Login</TableHead>
                  <TableHead className="w-12" />
                </TableRow>
              </TableHeader>
              <TableBody>
                {filteredUsers.map((user) => (
                  <TableRow key={user.id}>
                    <TableCell>
                      <div className="flex items-center gap-3">
                        <div className="flex h-9 w-9 items-center justify-center rounded-full bg-accent-600/20 text-sm font-bold text-accent-400">{user.displayName.charAt(0).toUpperCase()}</div>
                        <div>
                          <p className="font-medium text-white">{user.displayName}</p>
                          <p className="text-xs text-admin-400">{user.email}</p>
                        </div>
                      </div>
                    </TableCell>
                    <TableCell><RoleBadge role={user.role} /></TableCell>
                    <TableCell><StatusBadge status={user.status} /></TableCell>
                    <TableCell className="text-sm text-admin-400">{new Date(user.createdAt).toLocaleDateString()}</TableCell>
                    <TableCell className="text-sm text-admin-400">{user.lastLogin ? new Date(user.lastLogin).toLocaleDateString() : "Never"}</TableCell>
                    <TableCell>
                      <div className="relative">
                        <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => setActionMenuId(actionMenuId === user.id ? null : user.id)}>
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                        {actionMenuId === user.id && (
                          <div className="absolute right-0 top-9 z-50 w-40 rounded-lg border border-admin-700 bg-admin-800 py-1 shadow-xl">
                            <button className="flex w-full items-center gap-2 px-3 py-2 text-sm text-admin-200 hover:bg-admin-700" onClick={() => setActionMenuId(null)}>
                              <Edit2 className="h-3.5 w-3.5" />Edit User
                            </button>
                            <button className="flex w-full items-center gap-2 px-3 py-2 text-sm text-danger hover:bg-admin-700" onClick={() => { deleteMutation.mutate(user.id); setActionMenuId(null); }}>
                              <Trash2 className="h-3.5 w-3.5" />Delete User
                            </button>
                          </div>
                        )}
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>

      {showCreateModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm">
          <div className="w-full max-w-md rounded-xl border border-admin-700 bg-admin-800 shadow-2xl">
            <div className="flex items-center justify-between border-b border-admin-700 p-6">
              <h3 className="text-lg font-semibold text-white">Create New User</h3>
              <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => { setShowCreateModal(false); reset(); }}>
                <X className="h-4 w-4" />
              </Button>
            </div>
            <form onSubmit={handleSubmit(onCreateUser)} className="space-y-4 p-6">
              <div>
                <label className="mb-1.5 block text-sm font-medium text-admin-300">Email Address</label>
                <Input {...register("email")} placeholder="user@example.com" />
                {errors.email && <p className="mt-1 text-xs text-danger">{errors.email.message}</p>}
              </div>
              <div>
                <label className="mb-1.5 block text-sm font-medium text-admin-300">Display Name</label>
                <Input {...register("displayName")} placeholder="John Doe" />
                {errors.displayName && <p className="mt-1 text-xs text-danger">{errors.displayName.message}</p>}
              </div>
              <div>
                <label className="mb-1.5 block text-sm font-medium text-admin-300">Role</label>
                <select {...register("role")} className="flex h-10 w-full rounded-md border border-admin-600 bg-admin-900 px-3 py-2 text-sm text-admin-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-400">
                  <option value="user">User</option>
                  <option value="manager">Manager</option>
                  <option value="admin">Admin</option>
                </select>
              </div>
              <div className="flex justify-end gap-3 pt-2">
                <Button type="button" variant="outline" onClick={() => { setShowCreateModal(false); reset(); }}>Cancel</Button>
                <Button type="submit" disabled={createMutation.isPending}>
                  {createMutation.isPending && <Loader2 className="mr-2 h-4 w-4 animate-spin" />}
                  Create User
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
}
