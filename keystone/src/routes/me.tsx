import { createFileRoute } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { Fingerprint, Loader2, Server, ShieldCheck } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { SignInPrompt } from "@/components/auth/SignInPrompt";
import { useAuth } from "@/hooks/useAuth";
import { api, ApiError } from "@/lib/api";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/me")({ component: WhoAmIPage });

interface Claim {
  type: string;
  value: string;
}

interface WhoAmIResponse {
  claims: Claim[];
}

// JWT time claims are seconds since 1970; show them as readable dates.
const TIME_CLAIMS = new Set(["exp", "iat", "nbf", "auth_time"]);

function formatValue(claim: Claim): string {
  if (TIME_CLAIMS.has(claim.type) && /^\d+$/.test(claim.value)) {
    return `${new Date(Number(claim.value) * 1000).toLocaleString()} (${claim.value})`;
  }
  return claim.value;
}

function WhoAmIPage() {
  const { user, isAuthenticated, isLoading } = useAuth();

  // Goes BFF -> (token exchange) -> Core API. The BFF's own /me never reaches the Core API.
  const whoami = useQuery<WhoAmIResponse, ApiError>({
    queryKey: ["whoami"],
    queryFn: () => api.get<WhoAmIResponse>("/whoami"),
    enabled: isAuthenticated,
    retry: false,
  });

  if (isLoading) {
    return <Loader2 className="mx-auto mt-12 h-8 w-8 animate-spin text-slate-400" />;
  }

  if (!isAuthenticated || !user) {
    return <SignInPrompt message="Sign in to see who the platform thinks you are." />;
  }

  const sessionTypes = new Set(user.claims.map((c) => c.type));
  const apiTypes = new Set(whoami.data?.claims.map((c) => c.type) ?? []);

  return (
    <div className="mx-auto max-w-6xl space-y-6">
      <div>
        <h1 className="flex items-center gap-2 text-2xl font-bold text-slate-900">
          <Fingerprint className="h-6 w-6 text-primary-600" />
          Who am I?
        </h1>
        <p className="mt-1 text-sm text-slate-500">
          The same person, seen from two places. Claims marked <OnlyHere /> appear on one side only.
        </p>
      </div>

      <div className="grid gap-6 lg:grid-cols-2">
        <ClaimsCard
          icon={ShieldCheck}
          title="BFF session"
          subtitle="Zitadel's ID token and userinfo, kept in the BFF's Redis session. Answered by GET /api/keystone/me without calling any other service."
          claims={user.claims}
          otherTypes={apiTypes}
        />

        {whoami.isLoading ? (
          <Card>
            <CardContent className="p-10">
              <Loader2 className="mx-auto h-6 w-6 animate-spin text-slate-400" />
            </CardContent>
          </Card>
        ) : whoami.isError ? (
          <Card>
            <CardHeader>
              <CardTitle>Core API</CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-red-600">
                GET /api/keystone/whoami failed with {whoami.error.status}: {whoami.error.message}
              </p>
            </CardContent>
          </Card>
        ) : (
          <ClaimsCard
            icon={Server}
            title="Core API"
            subtitle="The Keystone access token the Identity Service issued in exchange for Zitadel's token. Answered by GET /api/whoami on the Core API."
            claims={whoami.data?.claims ?? []}
            otherTypes={sessionTypes}
          />
        )}
      </div>
    </div>
  );
}

function OnlyHere() {
  return (
    <span className="rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-800">only here</span>
  );
}

function ClaimsCard({
  icon: Icon,
  title,
  subtitle,
  claims,
  otherTypes,
}: {
  icon: typeof Server;
  title: string;
  subtitle: string;
  claims: Claim[];
  otherTypes: Set<string>;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Icon className="h-5 w-5 text-slate-400" />
          {title}
        </CardTitle>
        <p className="text-sm text-slate-500">{subtitle}</p>
      </CardHeader>
      <CardContent>
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-xs uppercase tracking-wider text-slate-400">
              <th className="py-2 pr-4 font-medium">Claim</th>
              <th className="py-2 font-medium">Value</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {claims.map((claim, index) => (
              <tr key={`${claim.type}-${index}`} className={cn(!otherTypes.has(claim.type) && "bg-amber-50/60")}>
                <td className="py-2 pr-4 align-top font-mono text-xs text-slate-700">
                  {claim.type} {!otherTypes.has(claim.type) && <OnlyHere />}
                </td>
                <td className="break-all py-2 font-mono text-xs text-slate-600">{formatValue(claim)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </CardContent>
    </Card>
  );
}
