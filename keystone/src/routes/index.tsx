import { createFileRoute, Link } from "@tanstack/react-router";
import { motion } from "framer-motion";
import { ArrowRight, BarChart3, Shield, Zap } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";

export const Route = createFileRoute("/")({
  component: HomePage,
});

const features = [
  {
    icon: Zap,
    title: "Lightning Fast",
    description:
      "Deploy your projects instantly with our optimized infrastructure and global CDN.",
  },
  {
    icon: Shield,
    title: "Enterprise Security",
    description:
      "Bank-grade encryption, SSO integration, and comprehensive audit logs for your team.",
  },
  {
    icon: BarChart3,
    title: "Real-time Analytics",
    description:
      "Monitor performance metrics, track usage patterns, and make data-driven decisions.",
  },
];

function HomePage() {
  return (
    <div className="mx-auto max-w-5xl space-y-12">
      {/* Hero Section */}
      <motion.section
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5 }}
        className="space-y-6 pt-8 text-center"
      >
        <h1 className="text-4xl font-bold tracking-tight text-slate-900 sm:text-5xl">
          Welcome to Your{" "}
          <span className="text-primary-600">Customer Portal</span>
        </h1>
        <p className="mx-auto max-w-2xl text-lg text-slate-600">
          Manage your projects, monitor deployments, and collaborate with your
          team -- all from one centralized dashboard.
        </p>
        <div className="flex items-center justify-center gap-4">
          <Link to="/dashboard">
            <Button size="lg">
              Go to Dashboard
              <ArrowRight className="ml-2 h-4 w-4" />
            </Button>
          </Link>
        </div>
      </motion.section>

      {/* Features */}
      <motion.section
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.2 }}
        className="grid gap-6 sm:grid-cols-3"
      >
        {features.map((feature) => (
          <Card key={feature.title} className="text-center">
            <CardHeader>
              <div className="mx-auto mb-2 flex h-12 w-12 items-center justify-center rounded-lg bg-primary-100 text-primary-600">
                <feature.icon className="h-6 w-6" />
              </div>
              <CardTitle className="text-lg">{feature.title}</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-slate-600">{feature.description}</p>
            </CardContent>
          </Card>
        ))}
      </motion.section>

      {/* Quick Stats */}
      <motion.section
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, delay: 0.4 }}
        className="rounded-2xl border border-border bg-surface p-8"
      >
        <h2 className="mb-6 text-center text-2xl font-semibold text-slate-900">
          Platform Overview
        </h2>
        <div className="grid grid-cols-2 gap-8 sm:grid-cols-4">
          {[
            { label: "Active Projects", value: "24" },
            { label: "Deployments", value: "1,280" },
            { label: "Uptime", value: "99.98%" },
            { label: "Team Members", value: "18" },
          ].map((stat) => (
            <div key={stat.label} className="text-center">
              <p className="text-3xl font-bold text-primary-600">
                {stat.value}
              </p>
              <p className="mt-1 text-sm text-slate-500">{stat.label}</p>
            </div>
          ))}
        </div>
      </motion.section>
    </div>
  );
}
