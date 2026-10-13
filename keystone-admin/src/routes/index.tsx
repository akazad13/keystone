import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { api } from "@/lib/api";
import type { DashboardStats } from "@/lib/schemas";
import { Users, UserCheck, UserPlus, Activity, TrendingUp, ArrowUpRight, ArrowDownRight } from "lucide-react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, BarChart, Bar } from "recharts";

export const Route = createFileRoute("/")({ component: DashboardPage });

const mockActivityData = [
  { name: "Mon", users: 120, sessions: 340 },
  { name: "Tue", users: 150, sessions: 420 },
  { name: "Wed", users: 180, sessions: 510 },
  { name: "Thu", users: 140, sessions: 380 },
  { name: "Fri", users: 200, sessions: 580 },
  { name: "Sat", users: 90, sessions: 220 },
  { name: "Sun", users: 70, sessions: 180 },
];

const mockRoleDistribution = [
  { name: "User", count: 1240 },
  { name: "Manager", count: 85 },
  { name: "Admin", count: 12 },
];

const mockRecentActivity = [
  { id: 1, user: "john@example.com", action: "User created", time: "2 min ago" },
  { id: 2, user: "jane@example.com", action: "Role changed to Manager", time: "15 min ago" },
  { id: 3, user: "bob@example.com", action: "Account suspended", time: "1 hour ago" },
  { id: 4, user: "alice@example.com", action: "Password reset", time: "2 hours ago" },
  { id: 5, user: "charlie@example.com", action: "User created", time: "3 hours ago" },
];

function DashboardPage() {
  const { data: stats } = useQuery<DashboardStats>({
    queryKey: ["dashboard", "stats"],
    queryFn: () => api.get<DashboardStats>("/dashboard/stats"),
    placeholderData: {
      totalUsers: 1337,
      activeUsers: 892,
      newUsersToday: 24,
      totalSessions: 3420,
    },
  });

  const statCards = [
    { title: "Total Users", value: stats?.totalUsers.toLocaleString() ?? "--", change: "+12.5%", trending: "up" as const, icon: Users, color: "text-blue-400", bgColor: "bg-blue-400/10" },
    { title: "Active Users", value: stats?.activeUsers.toLocaleString() ?? "--", change: "+8.2%", trending: "up" as const, icon: UserCheck, color: "text-emerald-400", bgColor: "bg-emerald-400/10" },
    { title: "New Today", value: stats?.newUsersToday.toLocaleString() ?? "--", change: "-3.1%", trending: "down" as const, icon: UserPlus, color: "text-amber-400", bgColor: "bg-amber-400/10" },
    { title: "Total Sessions", value: stats?.totalSessions.toLocaleString() ?? "--", change: "+18.7%", trending: "up" as const, icon: Activity, color: "text-purple-400", bgColor: "bg-purple-400/10" },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-2xl font-bold text-white">Dashboard Overview</h2>
        <p className="mt-1 text-sm text-admin-400">Monitor your platform activity and manage users.</p>
      </div>

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {statCards.map((stat) => {
          const Icon = stat.icon;
          return (
            <Card key={stat.title}>
              <CardContent className="p-6">
                <div className="flex items-center justify-between">
                  <div className={`rounded-lg p-2.5 ${stat.bgColor}`}>
                    <Icon className={`h-5 w-5 ${stat.color}`} />
                  </div>
                  <div className={`flex items-center gap-1 text-xs font-medium ${stat.trending === "up" ? "text-success" : "text-danger"}`}>
                    {stat.trending === "up" ? <ArrowUpRight className="h-3 w-3" /> : <ArrowDownRight className="h-3 w-3" />}
                    {stat.change}
                  </div>
                </div>
                <div className="mt-4">
                  <p className="text-2xl font-bold text-white">{stat.value}</p>
                  <p className="text-xs text-admin-400">{stat.title}</p>
                </div>
              </CardContent>
            </Card>
          );
        })}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <Card className="lg:col-span-2">
          <CardHeader>
            <div className="flex items-center justify-between">
              <CardTitle className="flex items-center gap-2">
                <TrendingUp className="h-5 w-5 text-accent-400" />
                Weekly Activity
              </CardTitle>
              <span className="text-xs text-admin-400">Last 7 days</span>
            </div>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <AreaChart data={mockActivityData}>
                <defs>
                  <linearGradient id="colorUsers" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#6366f1" stopOpacity={0.3} />
                    <stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                  </linearGradient>
                  <linearGradient id="colorSessions" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#22c55e" stopOpacity={0.3} />
                    <stop offset="95%" stopColor="#22c55e" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis dataKey="name" stroke="#64748b" fontSize={12} />
                <YAxis stroke="#64748b" fontSize={12} />
                <Tooltip contentStyle={{ backgroundColor: "#1e293b", border: "1px solid #334155", borderRadius: "8px", color: "#f1f5f9" }} />
                <Area type="monotone" dataKey="users" stroke="#6366f1" fillOpacity={1} fill="url(#colorUsers)" />
                <Area type="monotone" dataKey="sessions" stroke="#22c55e" fillOpacity={1} fill="url(#colorSessions)" />
              </AreaChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Role Distribution</CardTitle>
          </CardHeader>
          <CardContent>
            <ResponsiveContainer width="100%" height={280}>
              <BarChart data={mockRoleDistribution} layout="vertical">
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" />
                <XAxis type="number" stroke="#64748b" fontSize={12} />
                <YAxis dataKey="name" type="category" stroke="#64748b" fontSize={12} width={70} />
                <Tooltip contentStyle={{ backgroundColor: "#1e293b", border: "1px solid #334155", borderRadius: "8px", color: "#f1f5f9" }} />
                <Bar dataKey="count" fill="#6366f1" radius={[0, 4, 4, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </CardContent>
        </Card>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Recent Activity</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="space-y-4">
            {mockRecentActivity.map((activity) => (
              <div key={activity.id} className="flex items-center justify-between rounded-lg border border-admin-700/50 bg-admin-900/50 px-4 py-3">
                <div className="flex items-center gap-3">
                  <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent-600/20 text-xs font-bold text-accent-400">
                    {activity.user.charAt(0).toUpperCase()}
                  </div>
                  <div>
                    <p className="text-sm font-medium text-admin-200">{activity.user}</p>
                    <p className="text-xs text-admin-400">{activity.action}</p>
                  </div>
                </div>
                <span className="text-xs text-admin-500">{activity.time}</span>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
