import { createFileRoute } from "@tanstack/react-router";
import { motion } from "framer-motion";
import { Activity, FolderOpen, Rocket, TrendingUp, Users } from "lucide-react";
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, BarChart, Bar } from "recharts";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export const Route = createFileRoute("/dashboard")({ component: DashboardPage });

const monthlyData = [
  { month: "Jul", requests: 4200, deployments: 18 },
  { month: "Aug", requests: 5800, deployments: 24 },
  { month: "Sep", requests: 4900, deployments: 20 },
  { month: "Oct", requests: 7200, deployments: 32 },
  { month: "Nov", requests: 6100, deployments: 28 },
  { month: "Dec", requests: 8400, deployments: 36 },
  { month: "Jan", requests: 7600, deployments: 30 },
  { month: "Feb", requests: 9100, deployments: 42 },
  { month: "Mar", requests: 8800, deployments: 38 },
];

const recentActivities = [
  { id: "1", action: "Deployed", project: "E-commerce API", user: "Sarah Chen", time: "2 minutes ago" },
  { id: "2", action: "Updated", project: "Auth Service", user: "Marcus Johnson", time: "15 minutes ago" },
  { id: "3", action: "Created", project: "Analytics Dashboard", user: "Emily Rodriguez", time: "1 hour ago" },
  { id: "4", action: "Deployed", project: "Payment Gateway", user: "David Kim", time: "3 hours ago" },
  { id: "5", action: "Commented", project: "Mobile Backend", user: "Lisa Wang", time: "5 hours ago" },
];

const stats = [
  { title: "Total Projects", value: "24", change: "+3 this month", icon: FolderOpen, color: "text-blue-600", bg: "bg-blue-100" },
  { title: "Deployments", value: "1,280", change: "+42 this week", icon: Rocket, color: "text-emerald-600", bg: "bg-emerald-100" },
  { title: "Team Members", value: "18", change: "+2 new", icon: Users, color: "text-violet-600", bg: "bg-violet-100" },
  { title: "Uptime", value: "99.98%", change: "Last 30 days", icon: TrendingUp, color: "text-amber-600", bg: "bg-amber-100" },
];

function DashboardPage() {
  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-slate-900">Dashboard</h1>
        <p className="text-sm text-slate-500">Overview of your projects and activity</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {stats.map((stat, index) => (
          <motion.div key={stat.title} initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.3, delay: index * 0.1 }}>
            <Card>
              <CardContent className="p-5">
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-slate-500">{stat.title}</p>
                    <p className="mt-1 text-2xl font-bold text-slate-900">{stat.value}</p>
                    <p className="mt-1 text-xs text-slate-400">{stat.change}</p>
                  </div>
                  <div className={`flex h-10 w-10 items-center justify-center rounded-lg ${stat.bg}`}>
                    <stat.icon className={`h-5 w-5 ${stat.color}`} />
                  </div>
                </div>
              </CardContent>
            </Card>
          </motion.div>
        ))}
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: 0.3 }}>
          <Card>
            <CardHeader><CardTitle>API Requests</CardTitle></CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={280}>
                <AreaChart data={monthlyData}>
                  <defs>
                    <linearGradient id="requestsGradient" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#3b82f6" stopOpacity={0.3} />
                      <stop offset="100%" stopColor="#3b82f6" stopOpacity={0.0} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="month" tick={{ fontSize: 12, fill: "#94a3b8" }} />
                  <YAxis tick={{ fontSize: 12, fill: "#94a3b8" }} />
                  <Tooltip />
                  <Area type="monotone" dataKey="requests" stroke="#3b82f6" strokeWidth={2} fill="url(#requestsGradient)" />
                </AreaChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        </motion.div>

        <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: 0.4 }}>
          <Card>
            <CardHeader><CardTitle>Deployments</CardTitle></CardHeader>
            <CardContent>
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={monthlyData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
                  <XAxis dataKey="month" tick={{ fontSize: 12, fill: "#94a3b8" }} />
                  <YAxis tick={{ fontSize: 12, fill: "#94a3b8" }} />
                  <Tooltip />
                  <Bar dataKey="deployments" fill="#8b5cf6" radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            </CardContent>
          </Card>
        </motion.div>
      </div>

      <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: 0.5 }}>
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Activity className="h-5 w-5 text-slate-400" />
              Recent Activity
            </CardTitle>
          </CardHeader>
          <CardContent>
            <div className="divide-y divide-border">
              {recentActivities.map((activity) => (
                <div key={activity.id} className="flex items-center justify-between py-3 first:pt-0 last:pb-0">
                  <div>
                    <p className="text-sm font-medium text-slate-900">
                      <span className="text-primary-600">{activity.user}</span>{" "}
                      {activity.action.toLowerCase()}{" "}
                      <span className="font-semibold">{activity.project}</span>
                    </p>
                  </div>
                  <span className="text-xs text-slate-400 whitespace-nowrap ml-4">{activity.time}</span>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      </motion.div>
    </div>
  );
}
