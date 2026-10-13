import { createFileRoute, Link } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Activity, FolderKanban, FolderOpen, Shield, UserPlus, Users } from "lucide-react";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { Forbidden } from "@/components/auth/Forbidden";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api, ApiError } from "@/lib/api";
import type { AdminStats, AdminUser } from "@/lib/schemas";

export const Route = createFileRoute("/")({ component: DashboardPage });

const tooltipStyle = { backgroundColor: "#1e293b", border: "1px solid #334155", borderRadius: "8px", color: "#f1f5f9" };

function DashboardPage() {
  const stats = useQuery<AdminStats, ApiError>({
    queryKey: ["admin", "stats"],
    queryFn: () => api.get<AdminStats>("/admin/stats"),
    retry: false,
  });

  // Same query key as the Users page, so both share one cached response.
  const users = useQuery<AdminUser[], ApiError>({
    queryKey: ["admin", "users"],
    queryFn: () => api.get<AdminUser[]>("/admin/users"),
    retry: false,
  });

  if (stats.error?.status === 403 || users.error?.status === 403) {
    return <Forbidden />;
  }

  const s = stats.data;
  const statCards = [
    { title: "Total users", value: s?.totalUsers, hint: `${s?.admins ?? "--"} admins`, icon: Users, color: "text-blue-400", bgColor: "bg-blue-400/10" },
    { title: "Active users", value: s?.activeUsers, hint: "seen in the last 7 days", icon: Activity, color: "text-emerald-400", bgColor: "bg-emerald-400/10" },
    { title: "New today", value: s?.newUsersToday, hint: "first API call today", icon: UserPlus, color: "text-amber-400", bgColor: "bg-amber-400/10" },
    { title: "Projects", value: s?.totalProjects, hint: `${s?.activeProjects ?? "--"} active`, icon: FolderOpen, color: "text-purple-400", bgColor: "bg-purple-400/10" },
  ];

  const roleDistribution = Object.entries(
    (users.data ?? []).reduce<Record<string, number>>((counts, user) => {
      counts[user.role] = (counts[user.role] ?? 0) + 1;
      return counts;
    }, {})
  ).map(([name, count]) => ({ name, count }));

  const recentlySeen = (users.data ?? []).slice(0, 5);

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-white">Dashboard Overview</h2>
        <p className="mt-1 text-sm text-admin-400">Live numbers from the Core API.</p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {statCards.map((stat) => (
          <Card key={stat.title}>
            <CardContent className="p-6">
              <div className={`inline-flex rounded-lg p-2.5 ${stat.bgColor}`}>
                <stat.icon className={`h-5 w-5 ${stat.color}`} />
              </div>
              <div className="mt-4">
                <p className="text-2xl font-bold text-white">{stat.value?.toLocaleString() ?? "--"}</p>
                <p className="text-xs text-admin-400">{stat.title}</p>
                <p className="mt-1 text-xs text-admin-500">{stat.hint}</p>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Shield className="h-5 w-5 text-accent-400" />
              Roles
            </CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={220}>
              <BarChart data={roleDistribution} layout="vertical">
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis type="number" allowDecimals={false} stroke="#64748b" fontSize={12} />
                <YAxis dataKey="name" type="category" stroke="#64748b" fontSize={12} width={70} />
                <Tooltip contentStyle={tooltipStyle} />
                <Bar dataKey="count" fill="#6366f1" radius={[0, 4, 4, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card className="lg:col-span-2">
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center gap-2">
                <FolderKanban className="h-5 w-5 text-accent-400" />
                Recently active
              </CardTitle>
              <Link to="/users" className="text-xs text-accent-400 hover:underline">
                All users
              </Link>
            </div>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {recentlySeen.map((user) => (
                <div
                  key={user.id}
                  className="flex items-center justify-between rounded-lg border border-admin-700/50 bg-admin-900/50 px-4 py-3"
                >
                  <div className="flex items-center gap-3">
                    <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent-600/20 text-xs font-bold text-accent-400">
                      {(user.displayName || user.email).charAt(0).toUpperCase()}
                    </div>
                    <div>
                      <p className="text-sm font-medium text-admin-200">{user.displayName}</p>
                      <p className="text-xs text-admin-400">
                        {user.role} · {user.projectCount} project{user.projectCount === 1 ? "" : "s"}
                      </p>
                    </div>
                  </div>
                  <span className="text-xs text-admin-500">{new Date(user.lastSeenAt).toLocaleString()}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
